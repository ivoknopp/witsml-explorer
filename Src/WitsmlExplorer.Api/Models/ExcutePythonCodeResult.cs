namespace WitsmlExplorer.Api.Models
{
    public class ExcutePythonCodeResult
    {
        public string Output { get; set; }
        public string ErrorMessage { get; set; }
        public bool IsSuccess { get; set; }
    }
}
