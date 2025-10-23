namespace WitsmlExplorer.Api.Models
{
    public class OpenAiServiceResult
    {
        public string ErrorMessage { get; set; }
        public bool IsSuccess { get; set; } = true;
        public string GeneratedPythonCode { get; set; }
        public long ModelExecTimeMs { get; set; }
        public long PythonExecTimeMs { get; set; }
        public string FileId { get; set; }
        public string UsedModelName { get; set; }
    }
}
