using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

using Amazon.Runtime.Internal.Endpoints.StandardLibrary;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

using SharpCompress.Common;

using WitsmlExplorer.Api.Jobs;
using WitsmlExplorer.Api.Models;


namespace WitsmlExplorer.Api.Services
{
    public interface IOpenAIService
    {
        Task<OpenAiServiceResult> QueryCsvFile(string fileId, string userText);
    }

    public class Enums
    {
        public enum AiQueryType
        {
            Csv,
            MCsv,
            Plot
        }
    }

    public class OpenAIService : IOpenAIService
    {
        const string CONTAINER_MAPPED_DATA_DIR = @"/data";
        const int TEMP_FILES_RETENTION_HOURS_DEFAULT = 1;
        const int LOG_FILES_RETENTION_HOURS_DEFAULT = 1;
        const string RESULT_FILE_SUFFIX = "_result";
        string _tempDataDir;
        string _openAiLogDir;
        string _openAiApiKey;
        string _usedModelName;
        int _tempFilesRetentionHours;
        int _logFilesRetentionHours;

        IPythonCodeExecutionService _pythonExecService;
        IConfiguration _configuration;

        public OpenAIService(IPythonCodeExecutionService pythonCodeExecutionService, IConfiguration configuration)
        {
            _pythonExecService = pythonCodeExecutionService;
            _tempDataDir = configuration["TempDataStorageDir"];
            _openAiLogDir = configuration["OpenAiLogDir"];
            _openAiApiKey = configuration["OpenAiApiKey"];   
            _usedModelName = configuration["OpenAiModelName"];
            _tempFilesRetentionHours = configuration.GetValue<int>("TempFilesRetentionHours", TEMP_FILES_RETENTION_HOURS_DEFAULT);
            _logFilesRetentionHours = configuration.GetValue<int>("LogFilesRetentionHours", LOG_FILES_RETENTION_HOURS_DEFAULT);
        }

        public async Task<OpenAiServiceResult> QueryCsvFile(string fileId, string userText)
        {
            FilesCleanup(_tempDataDir, _tempFilesRetentionHours);
            FilesCleanup(_openAiLogDir, _logFilesRetentionHours);
            ValidateParametersAndSettings(fileId, userText);

            return await Execute(fileId, userText, Enums.AiQueryType.Csv);
        }

        private void ValidateParametersAndSettings(string fileId, string userText)
        {
            if (string.IsNullOrWhiteSpace(userText))
            {
                throw new ArgumentException($"User text cannot be null or empty, {userText}");
            }

            if (string.IsNullOrWhiteSpace(fileId))
            {
                throw new ArgumentException($"File ID cannot be null or empty, {fileId}");
            }

            if (!Directory.Exists(_tempDataDir))
            {
                throw new DirectoryNotFoundException($"Temporary data directory does not exist: {_tempDataDir}");
            }

            var filePath = Path.Combine(_tempDataDir, $"{fileId}.csv");
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"File does not exist: {filePath}");
            }

            EnsureConfigured(_openAiApiKey, "OpenAI API key");
            EnsureConfigured(_usedModelName, "OpenAI deployment name");
            EnsureConfigured(_tempDataDir, "Temporary data directory");
        }

        private void EnsureConfigured(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"{name} is not configured.");
            }
        }

        private async Task<OpenAiServiceResult> Execute(string fileId, string userText, Enums.AiQueryType queryType)
        {
            var prompt = GetPrompt(userText, _tempDataDir, fileId);
            var kernel = CreateSemanticKernel();

            Stopwatch stopwatch = Stopwatch.StartNew();
            var llmResult = await kernel.InvokePromptAsync(prompt);
            var kernelEllapsedMs = stopwatch.ElapsedMilliseconds;

            stopwatch.Restart();
            var pythonCode = ExtractFromResult(llmResult.ToString());
            var pythonExecutionResult = await _pythonExecService.ExecutePythonCode(pythonCode, CONTAINER_MAPPED_DATA_DIR);
            var pythonExecEllapsedMs = stopwatch.ElapsedMilliseconds;

            LogExecutionInformation(pythonCode,
                            pythonExecutionResult,
                            userText,
                            new List<string> { fileId },
                            fileId,
                            kernelEllapsedMs,
                            pythonExecEllapsedMs,
                            _usedModelName,
                            queryType,
                            prompt);

            var result = new OpenAiServiceResult()
            {
                IsSuccess = pythonExecutionResult.IsSuccess,
                ErrorMessage = pythonExecutionResult.ErrorMessage,
                GeneratedPythonCode = pythonCode,
                ModelExecTimeMs = kernelEllapsedMs,
                PythonExecTimeMs = pythonExecEllapsedMs,
                UsedModelName = _usedModelName
            };
            return result;
        }

        private Kernel CreateSemanticKernel()
        {
            var builder = Kernel.CreateBuilder();

            builder.Services.AddOpenAIChatCompletion(
                _usedModelName,
                _openAiApiKey);

            return builder.Build();
        }

        private static string ExtractFromResult(string result)
        {
            var pythonCode = result.ToString().Replace("```python", "").Replace("```", "").Trim();
            return pythonCode;
        }

        private string GetPrompt(string userText, string dataDir, string fileId)
        {
            var specFilePath = Path.Combine(dataDir, fileId + "_spec.json");
            var metadata = File.ReadAllText(specFilePath);

            return $@"
            system:
            You are a helpful assistant that writes Python code and uses Pandas library to get information from CSV data.

            ## Instructions:

            - The columns in csv data is described as json structure (surrounded by three backticks below) 
               - The Mnemonic represents column name.
               - Use Unit element for determining if column is of ISO 8601 date/time format

            ```
            {metadata}
            ```

            - Write Python code that:
                - Reads the {fileId}.csv file from directory {CONTAINER_MAPPED_DATA_DIR}
                - Uses the `pandas` library to read the CSV files
                - Performs the user-requested operation, see below a text surrounded by three backticks
                - Writes the result to `data_dir` as a CSV file named `{fileId}{RESULT_FILE_SUFFIX}.csv`
                - Always includes the header in the output CSV with column names
                - Uses the `pandas` library for CSV operations
                - For pandas read_csv use always engine='c'
                - If the code contains any file path then add r before the path string to avoid escape character issues
                - Do not use method data.resample
                - If output column is datetime, convert it to ISO 8601 date/time format (e.g. 2023-04-19T07:28:31.0000000Z)
                - If there is request for any aggregation, e.g. count, sum, max then use the aggregation function name in the header column
                - If there is a request that expects answer as boolean, then for the header use the user query in some shortened version
                - If the user-requested operation is not relevant to the data then return only header and no data    
            - User text notes:
                - convert and replace the date and time information to ISO 8601 date/time format (e.g. 2023-04-19T07:28:31.0000000Z) before usage

            ```
            {userText}
            ```

            - Respond with only the Python code. No extra text. Delete rows containing three backticks.    
            ";
        }

        private void LogExecutionInformation(string pythonCode,
            ExcutePythonCodeResult executionResult,
            string userText,
            List<string> inputFileIds,
            string resultFileId,
            long kernelEllapsedMs,
            long pythonExecEllapsedMs,
            string deploymentName,
            Enums.AiQueryType queryType,
            string prompt)
        {
            try
            {
                var sb = new StringBuilder();
                foreach (var fileId in inputFileIds)
                {
                    var inFileInfo = new FileInfo(Path.Combine(_tempDataDir, $"{fileId}.csv"));
                    double inFileSizeInMB = inFileInfo.Length / (1024.0 * 1024);
                    sb.AppendLine($"{fileId}.csv, ({inFileSizeInMB:F2} MB)");
                }

                var resultFileInfo = String.Empty;
                var outFilePath = Path.Combine(_tempDataDir, $"{resultFileId}{RESULT_FILE_SUFFIX}.csv");
                if (File.Exists(outFilePath))
                {
                    var fileInfo = new FileInfo(outFilePath);
                    double outFileSizeInMB = fileInfo.Length / (1024.0 * 1024);
                    resultFileInfo = $"{resultFileId}{RESULT_FILE_SUFFIX}.csv, ({outFileSizeInMB:F2}) MB";
                }

                var logText = RemoveSpaces($@"
                    User text:
                    {userText}

                    Data files information:
                    - Input file(s):
                      {sb.ToString()}

                    - Result file: {resultFileInfo}

                    Execution result:
                    {executionResult}

                    Execution times:
                    - Semantic kernel: {kernelEllapsedMs} ms
                    - Python execution: {pythonExecEllapsedMs} ms

                    Used model: {deploymentName}

                    ----------------------------------------------------------------
                    Generated python code:
                    {pythonCode}
                    ----------------------------------------------------------------
                    Prompt:
                    {prompt}
                ");

                if (!Directory.Exists(_openAiLogDir))
                {
                    Directory.CreateDirectory(_openAiLogDir);
                }
                File.WriteAllText(Path.Combine(_openAiLogDir, $"{queryType}_{resultFileId}_{DateTime.Now.ToString("yyyy-MM-dd_HH_mm_ss")}.txt"), logText);
            }
            catch (Exception ex)
            {
                throw new Exception($"OpenAiService execution failed. Error: {ex}");
            }
        }

        private static string RemoveSpaces(string text)
        {
            if (String.IsNullOrEmpty(text))
                return text;

            var lines = text.Split('\n');
            var trimmed = lines
                .Select(line => line.TrimStart()) // remove leading spaces per line
                .ToArray();
            return string.Join("\n", trimmed);
        }

        private void FilesCleanup(string dir, int retentionTimeHours)
        {
            try
            {
                var directory = new DirectoryInfo(dir);
                foreach (var file in directory.GetFiles("*.*"))
                {
                    if (file.CreationTime < DateTime.Now.AddHours(-retentionTimeHours))
                    {
                        file.Delete();
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Error while cleaning up data files, Exception: {ex}");
            }
        }
    }
}
