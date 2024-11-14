using System.Text;
using System.Xml;
using YamlDotNet.Serialization;
using CsvHelper;
using System.Globalization;
using FluentAssertions;

namespace EDMCsvGenerationE2ETests;

public class DRMWebServiceIntegrationTests
{
    private readonly HttpClient _httpClient = new();
    private const string TestCasesDirectory = "TestCases";
    private const string CsvOutputDirectory = "/path/to/csv/output";

    [Theory]
    [MemberData(nameof(LoadTestCases), parameters: TestCasesDirectory)]
    public async Task Test_YamlToSoapRequestToCsv(string endpoint, string soapAction, string yamlPayload, string expectedCsvContent)
    {
        // Convert YAML to XML for SOAP request body
        var soapXmlBody = ConvertYamlToSoapXml(yamlPayload);

        // Act: Call the endpoint with the SOAP request
        var response = await CallSoapEndpoint(endpoint, soapAction, soapXmlBody);

        response.IsSuccessStatusCode.Should().BeTrue("the SOAP response should be successful");

        await Task.Delay(2000);

        var actualCsvFile = Directory
            .GetFiles(CsvOutputDirectory, "*.csv")
            .OrderByDescending(File.GetCreationTime)
            .FirstOrDefault();

        // Assert
        actualCsvFile.Should().NotBeNull("CSV file should be created.");

        var csvOutputMatches = CompareCsvContent(expectedCsvContent, actualCsvFile!);

        csvOutputMatches.Should().BeTrue("The CSV content should match.");
    }

    public static IEnumerable<object[]> LoadTestCases(string folderPath)
    {
        foreach (var filePath in Directory.GetFiles(folderPath, "*.yaml"))
        {
            var deserializer = new DeserializerBuilder().Build();
            var yamlContent = File.ReadAllText(filePath);
            var testCase = deserializer.Deserialize<TestCase>(yamlContent);

            yield return
            [
                testCase.Endpoint,
                testCase.SoapAction,
                testCase.RequestPayload,
                testCase.ExpectedOutputCsv
            ];
        }
    }

    private string ConvertYamlToSoapXml(string yamlContent)
    {
        var deserializer = new Deserializer();
        var yamlObject = deserializer.Deserialize(new StringReader(yamlContent));

        var xmlDocument = new XmlDocument();
        var envelope = xmlDocument.CreateElement("soap", "Envelope", "http://schemas.xmlsoap.org/soap/envelope/");
        xmlDocument.AppendChild(envelope);

        var body = xmlDocument.CreateElement("soap", "Body", "http://schemas.xmlsoap.org/soap/envelope/");
        envelope.AppendChild(body);

        var uploadElement = xmlDocument.CreateElement("UploadExtendedLocalMasterData", "http://ipggz.MDM.org/");
        body.AppendChild(uploadElement);

        var localDataList = xmlDocument.CreateElement("localDataList");
        uploadElement.AppendChild(localDataList);

        ConvertYamlNodeToXml(yamlObject, xmlDocument, localDataList);
        return xmlDocument.OuterXml;
    }

    private static void ConvertYamlNodeToXml(object yamlObject, XmlDocument xmlDocument, XmlNode parentNode)
    {
        switch (yamlObject)
        {
            case Dictionary<object, object> dictionary:
            {
                foreach (var key in dictionary.Keys)
                {
                    var childElement = xmlDocument.CreateElement(key.ToString() ?? string.Empty);
                    parentNode.AppendChild(childElement);
                    ConvertYamlNodeToXml(dictionary[key], xmlDocument, childElement);
                }

                break;
            }
            case List<object> list:
            {
                foreach (var item in list)
                {
                    var itemElement = xmlDocument.CreateElement("ExtendedLocalDataVO");
                    parentNode.AppendChild(itemElement);
                    ConvertYamlNodeToXml(item, xmlDocument, itemElement);
                }

                break;
            }
            default:
                parentNode.InnerText = yamlObject?.ToString() ?? string.Empty;
                break;
        }
    }

    private async Task<HttpResponseMessage> CallSoapEndpoint(string endpoint, string soapAction, string soapXml)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(soapXml, Encoding.UTF8, "text/xml")
        };
        request.Headers.Add("SOAPAction", soapAction);
        return await _httpClient.SendAsync(request);
    }

    private bool CompareCsvContent(string expectedCsvContent, string actualCsvFilePath)
    {
        using var expectedReader = new StringReader(expectedCsvContent);
        using var actualReader = new StreamReader(actualCsvFilePath);

        using var expectedCsv = new CsvReader(expectedReader, CultureInfo.InvariantCulture);
        using var actualCsv = new CsvReader(actualReader, CultureInfo.InvariantCulture);

        var expectedRecords = expectedCsv.GetRecords<dynamic>().ToList();
        var actualRecords = actualCsv.GetRecords<dynamic>().ToList();

        return expectedRecords.SequenceEqual(actualRecords);
    }
    
    private class TestCase
    {
        public string Endpoint { get; set; } = string.Empty;
        public string SoapAction { get; set; } = string.Empty;
        public string RequestPayload { get; set; } = string.Empty;
        public string ExpectedOutputCsv { get; set; } = string.Empty;
    }
}