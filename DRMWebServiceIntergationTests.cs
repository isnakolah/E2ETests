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
    private const string TestCasesDirectory = "/Users/danielnakolah/RiderProjects/EDMCsvGenerationE2ETests/TestsCases";
    private const string CsvOutputDirectory = "/path/to/csv/output";

    [Theory]
    [MemberData(memberName: nameof(LoadTestCases), parameters: TestCasesDirectory)]
    public async Task Test_YamlToSoapRequestToCsv(string endpoint, string soapAction, string yamlPayload, string expectedCsvContent)
    {
        // Convert YAML to XML for SOAP request body
        var soapXmlBody = ConvertYamlToSoapXml(yamlContent: yamlPayload);

        // Act: Call the endpoint with the SOAP request
        var response = await CallSoapEndpoint(endpoint: endpoint, soapAction: soapAction, soapXml: soapXmlBody);

        response.IsSuccessStatusCode.Should().BeTrue(because: "the SOAP response should be successful");

        await Task.Delay(millisecondsDelay: 2000);

        var actualCsvFile = Directory
            .GetFiles(path: CsvOutputDirectory, searchPattern: "*.csv")
            .OrderByDescending(keySelector: File.GetCreationTime)
            .FirstOrDefault();

        // Assert
        actualCsvFile.Should().NotBeNull(because: "CSV file should be created.");

        var csvOutputMatches = CompareCsvContent(expectedCsvContent: expectedCsvContent, actualCsvFilePath: actualCsvFile!);

        csvOutputMatches.Should().BeTrue(because: "The CSV content should match.");
    }

    public static IEnumerable<object[]> LoadTestCases(string folderPath)
    {
        foreach (var filePath in Directory.GetFiles(path: folderPath, searchPattern: "*.yaml"))
        {
            var deserializer = new DeserializerBuilder().Build();
            var yamlContent = File.ReadAllText(path: filePath);
            var testCase = deserializer.Deserialize<TestCase>(input: yamlContent);

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
        var yamlObject = deserializer.Deserialize(input: new StringReader(s: yamlContent));

        var xmlDocument = new XmlDocument();

        var envelope = xmlDocument
            .CreateElement(
                prefix: "soap",
                localName: "Envelope",
                namespaceURI: "http://schemas.xmlsoap.org/soap/envelope/");

        xmlDocument.AppendChild(newChild: envelope);

        var body = xmlDocument.CreateElement(
            prefix: "soap",
            localName: "Body",
            namespaceURI: "http://schemas.xmlsoap.org/soap/envelope/");

        envelope.AppendChild(newChild: body);

        var uploadElement = xmlDocument.CreateElement(
            qualifiedName: "UploadExtendedLocalMasterData",
            namespaceURI: "http://ipggz.MDM.org/");

        body.AppendChild(newChild: uploadElement);

        var localDataList = xmlDocument.CreateElement(name: "localDataList");
        uploadElement.AppendChild(newChild: localDataList);

        ConvertYamlNodeToXml(
            yamlObject: yamlObject,
            xmlDocument: xmlDocument,
            parentNode: localDataList);

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
                    // Skip creating another localDataList if we already have one
                    if (key.ToString() == "localDataList" && parentNode.Name == "localDataList")
                    {
                        ConvertYamlNodeToXml(
                            yamlObject: dictionary[key],
                            xmlDocument: xmlDocument,
                            parentNode: parentNode);
                    }
                    else
                    {
                        var childElement = xmlDocument.CreateElement(name: key.ToString() ?? string.Empty);
                        parentNode.AppendChild(newChild: childElement);
                        ConvertYamlNodeToXml(
                            yamlObject: dictionary[key],
                            xmlDocument: xmlDocument,
                            parentNode: childElement);
                    }
                }
                break;
            }
            case List<object> list:
            {
                foreach (var item in list)
                {
                    var itemElement = xmlDocument.CreateElement(name: "ExtendedLocalDataVO");
                    parentNode.AppendChild(newChild: itemElement);
                    ConvertYamlNodeToXml(yamlObject: item, xmlDocument: xmlDocument, parentNode: itemElement);
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
        var request = new HttpRequestMessage(method: HttpMethod.Post, requestUri: endpoint)
        {
            Content = new StringContent(
                content: soapXml,
                encoding: Encoding.UTF8,
                mediaType: "text/xml")
        };

        request.Headers.Add(name: "SOAPAction", value: soapAction);

        return await _httpClient.SendAsync(request: request);
    }

    private static bool CompareCsvContent(string expectedCsvContent, string actualCsvFilePath)
    {
        using var expectedReader = new StringReader(s: expectedCsvContent);
        using var actualReader = new StreamReader(path: actualCsvFilePath);

        using var expectedCsv = new CsvReader(reader: expectedReader, culture: CultureInfo.InvariantCulture);
        using var actualCsv = new CsvReader(reader: actualReader, culture: CultureInfo.InvariantCulture);

        var expectedRecords = expectedCsv.GetRecords<dynamic>().ToList();
        var actualRecords = actualCsv.GetRecords<dynamic>().ToList();

        return expectedRecords.SequenceEqual(second: actualRecords);
    }
    
    private class TestCase
    {
        [YamlMember(Alias = "endpoint")]
        public string Endpoint { get; set; } = string.Empty;
        
        [YamlMember(Alias = "soap_action")]
        public string SoapAction { get; set; } = string.Empty;

        public string SoupMethod => SoapAction.Split('/').Last();

        [YamlMember(Alias = "request_payload")]
        public string RequestPayload { get; set; } = string.Empty;
        
        [YamlMember(Alias = "expected_output_csv_file_location")]
        public string ExpectedOutputCsvFileLocation { get; set; } = string.Empty;
        
        [YamlMember(Alias = "expected_output_csv")]
        public string ExpectedOutputCsv { get; set; } = string.Empty;
    }
}