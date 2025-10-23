using System.Text.Json.Serialization;
using System;
using System.Runtime.CompilerServices;

namespace WitsmlExplorer.Api.Models
{
    public class AiRequest
    {
        public string UserText { get; set; } = string.Empty;
        public string FileId { get; set; } = string.Empty;
    }
}
