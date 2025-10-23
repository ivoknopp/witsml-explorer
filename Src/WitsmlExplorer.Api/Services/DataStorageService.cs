using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Amazon.Runtime.SharedInterfaces;

using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

using SharpCompress.Common;

using ThirdParty.Json.LitJson;

using WitsmlExplorer.Api.Models;

namespace WitsmlExplorer.Api.Services
{
    public interface IDataStorageService
    {
        string StoreData(LogData logData, string[] dataIdentifiers);
        LogData LoadData(string[] dataIdentifiers, bool useAiResultData);

        string GetFileId(string[] args);
        bool IsOriginalDataStored(string fileId);
    }

    public class DataStorageService : IDataStorageService
    {
        private string _tempDataDir;

        public DataStorageService(IConfiguration configuration)
        {
            _tempDataDir = configuration["TempDataStorageDir"];
        }

        public string StoreData(LogData logData, string[] dataIdentifiers)
        {
            if (dataIdentifiers == null || dataIdentifiers.Length == 0 || logData == null)
            {
                return null;
            }

            var fileId = GetFileId(dataIdentifiers);
            var jsonData = JsonSerializer.Serialize(logData);
            StoreDataForAI(logData, fileId);
            return fileId;
        }

        public LogData LoadData(string[] dataIdentifiers, bool useAiPythonGeneratedResultData)
        {
            if (dataIdentifiers == null || dataIdentifiers.Length == 0)
            {
                return null;
            }

            var fileId = GetFileId(dataIdentifiers);
            var filePath = Path.Combine(_tempDataDir, fileId + ".csv");
            if (!File.Exists(filePath))
            {
                return null;
            }
            if (useAiPythonGeneratedResultData)
            {
                GenerateSpecFile(fileId);
            }


            return RestoreData(fileId, useAiPythonGeneratedResultData);
        }

        private void GenerateSpecFile(string fileId)
        {
            var specFilePath = Path.Combine(_tempDataDir, $"{fileId}_spec.json");
            var curveSpecifications = JsonSerializer.Deserialize<CurveSpecification[]>(File.ReadAllText(specFilePath));
            var curveSpecificationsDict = curveSpecifications.ToDictionary(x => x.Mnemonic, x => x.Unit);

            var dataFilepath = Path.Combine(_tempDataDir, $"{fileId}_result.csv");
            string[] headers;

            using (var reader = new StreamReader(dataFilepath))
            {
                string headerLine = reader.ReadLine();
                if (headerLine == null)
                {
                    return;
                }
                headers = headerLine.Split(',');
            }

            var result = new List<CurveSpecification>();

            foreach (var header in headers)
            {
                if (curveSpecificationsDict.TryGetValue(header, out var unit))
                {
                    var cs = new CurveSpecification()
                    {
                        Mnemonic = header,
                        Unit = unit
                    };
                    result.Add(cs);
                }
                else
                {
                    var cs = new CurveSpecification()
                    {
                        Mnemonic = header,
                        Unit = "unitless"
                    };
                    result.Add(cs);
                }
            }
            try
            {
                var jsonString = JsonSerializer.Serialize<CurveSpecification[]>(result.ToArray());
                File.WriteAllText(Path.Combine(_tempDataDir, fileId + "_spec_result.json"), jsonString);
            }
            catch (Exception ex)
            {
                throw new Exception($"Error generating spec file for AI result data: {ex}");
            }

        }

        public string GetFileId(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                return null;
            }   

            using var md5 = MD5.Create();
            byte[] hashBytes = md5.ComputeHash(Encoding.UTF8.GetBytes(string.Join("_", args)));
            string hashString = BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
            return hashString;
        }

        private LogData RestoreData(string fileId, bool useAiResultData)
        {
            var resultFileSuffix = useAiResultData ? "_result" : "";
            var specFilePath = Path.Combine(_tempDataDir, $"{fileId}_spec{resultFileSuffix}.json");
            var dataFilePath = Path.Combine(_tempDataDir, $"{fileId}{resultFileSuffix}.csv");
            var basicDataFilePath = Path.Combine(_tempDataDir, fileId + "_basic.json");

            var curveSpecifications = JsonSerializer.Deserialize<CurveSpecification[]>(File.ReadAllText(specFilePath));

            var data = new List<Dictionary<string, LogDataValue>>();
            var mnemonics = curveSpecifications.Select(x => x.Mnemonic).ToArray();
            var skipHeaderLine = true;

            foreach (var line in File.ReadLines(dataFilePath))
            {
                if (skipHeaderLine)
                {
                    skipHeaderLine = false;
                    continue;
                }

                var dataDict = new Dictionary<string, LogDataValue>();
                var values = line.Split(',').ToArray();
                for (int i = 0; i < mnemonics.Length; i++)
                {
                    dataDict[mnemonics[i]] = new LogDataValue(values[i]);
                }
                data.Add(dataDict);
            }

            var basicLogData = JsonSerializer.Deserialize<LogData>(File.ReadAllText(basicDataFilePath));

            var logData = new LogData()
            {
                StartIndex = basicLogData.StartIndex,
                EndIndex = basicLogData.EndIndex,
                Direction = basicLogData.Direction,
                CurveSpecifications = curveSpecifications,
                Data = data
            };

            return logData;
        }

        private void StoreDataForAI(LogData logData, string fileId)
        {
            FlattenDataToCsv(logData, fileId);
            CreateMetadata(logData, fileId);
            StoreBasicData(logData, fileId);
        }

        private void StoreBasicData(LogData logData, string fileId)
        {
            var basicLogData = new LogData()
            {
                StartIndex = logData.StartIndex,
                EndIndex = logData.EndIndex,
                Direction = logData.Direction,
                CurveSpecifications = null,
                Data = null
            };
            try
            {
                var jsonData = JsonSerializer.Serialize(basicLogData);
                File.WriteAllText(Path.Combine(_tempDataDir, fileId + "_basic.json"), jsonData);
            }
            catch(Exception ex)
            {
                throw new Exception($"Error storing basic log data for AI processing: {ex}");
            }

        }

        private void FlattenDataToCsv(LogData logData, string fileId)
        {
            try
            {
                using (StreamWriter sw = new StreamWriter(Path.Combine(_tempDataDir, fileId + ".csv")))
                {
                    var mnemonics = logData.CurveSpecifications.Select(x => x.Mnemonic);
                    var header = mnemonics.Aggregate((result, next) => result + "," + next);
                    sw.WriteLine(header);
                    var sbRow = new StringBuilder();
                    var i = 0;

                    foreach (var item in logData.Data)
                    {
                        foreach (var m in mnemonics)
                        {
                            if (sbRow.Length > 0)
                            {
                                sbRow.Append(",");
                            }

                            if (item.TryGetValue(m, out var value))
                            {
                                sbRow.Append(value.Value);
                            }
                            else
                            {
                                Console.WriteLine($"Warning: key '{m}' not found while writing CSV data, row {i}.");
                                sbRow.Append("0");
                            }
                        }
                        i++;
                        sw.WriteLine(sbRow.ToString());
                        sbRow.Clear();
                    }
                    sw.Flush();
                }
            }
            catch(Exception ex)
            {
                throw new Exception($"Error flattening log data to CSV for AI processing: {ex}");
            }

        }

        private void CreateMetadata(LogData logData, string fileId)
        {
            try
            {
                var jsonData = JsonSerializer.Serialize(logData.CurveSpecifications);
                File.WriteAllText(Path.Combine(_tempDataDir, fileId + "_spec.json"), jsonData);
            }
            catch(Exception ex)
            {
                throw new Exception($"Error creating metadata for AI processing: {ex}");
            }

        }

        public bool IsOriginalDataStored(string fileId)
        {
            if (fileId == null)
            {
                return false;
            }
            return File.Exists(Path.Combine(_tempDataDir, fileId + ".csv"));
        }
    }
}
