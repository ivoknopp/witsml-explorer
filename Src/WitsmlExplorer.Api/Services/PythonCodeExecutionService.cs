using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

using WitsmlExplorer.Api.Models;

using static System.Runtime.InteropServices.JavaScript.JSType;

namespace WitsmlExplorer.Api.Services
{
    public interface IPythonCodeExecutionService
    {
        Task<ExcutePythonCodeResult> ExecutePythonCode(string pythonCode, string containerMappedDataDir);
    }

    public class PythonCodeExecutionService : IPythonCodeExecutionService
    {
        private const int EXEC_MAX_TIME_DEFAULT = 30;
        private int _containerMaxExecTimeSeconds;

        public PythonCodeExecutionService(IConfiguration configuration)
        {
            _containerMaxExecTimeSeconds = configuration.GetValue<int>("PythonContainerMaxExecTimeSec", EXEC_MAX_TIME_DEFAULT);
        }

        public async Task<ExcutePythonCodeResult> ExecutePythonCode(string pythonCode, string containerMappedDataDir)
        {
            if (string.IsNullOrWhiteSpace(pythonCode))
                throw new ArgumentException("Python code cannot be null or empty.", nameof(pythonCode));

            if (string.IsNullOrWhiteSpace(containerMappedDataDir))
                throw new ArgumentException("Container mapped data directory cannot be null or empty.", nameof(containerMappedDataDir));

            var result = RunPythonSandbox(pythonCode, containerMappedDataDir);
            return await Task.FromResult(result);
        }

        private ExcutePythonCodeResult RunPythonSandbox(string code, string containerMappedDataDir)
        {
            string tmpPath = null;
            try
            {
                tmpPath = Path.GetTempFileName().Replace("\\", "/") + ".py";
                File.WriteAllText(tmpPath, code);

                // Build the Docker command
                string dockerArgs = $"run --rm " +
                                    $"--network none " +
                                    $"--memory 128m " +
                                    $"--cpus 0.5 " +
                                    $"-v \"{tmpPath}:/app/code.py:ro\" " +
                                    $"-v \"d:/tmp/witsml:{containerMappedDataDir}:rw\" " +
                                    $"python-sandbox:latest code.py";

                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "docker",
                        Arguments = dockerArgs,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                // Run Docker command
                process.Start();

                // Optional: add timeout logic
                if (!process.WaitForExit(_containerMaxExecTimeSeconds * 1000))
                {
                    try { process.Kill(); } catch { }
                    return new ExcutePythonCodeResult()
                    {
                        IsSuccess = false,
                        Output = string.Empty,
                        ErrorMessage = "Execution timed out"
                    };
                }

                var execResult = new ExcutePythonCodeResult()
                {
                    IsSuccess = process.ExitCode == 0,
                    Output = process.StandardOutput.ReadToEnd(),
                    ErrorMessage = process.StandardError.ReadToEnd()
                    //ErrorMessage = "Ivosek errorek"
                };

                return execResult;
            }
            catch (Exception ex)
            {
                return new ExcutePythonCodeResult()
                {
                    IsSuccess = false,
                    Output = string.Empty,
                    ErrorMessage = ex.Message
                };
            }
            finally
            {
                if (tmpPath != null && File.Exists(tmpPath))
                    File.Delete(tmpPath);
            }
        }
    }

}

