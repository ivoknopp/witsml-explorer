using System.Threading.Tasks;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using WitsmlExplorer.Api.Models;
using WitsmlExplorer.Api.Services;

namespace WitsmlExplorer.Api.HttpHandlers
{
    public class ChatHandler
    {
        [Produces(typeof(string))]
        public static async Task<IResult> QueryLogData([FromBody] DataQueryRequest request, IOpenAIService openAIService)
        {
            await openAIService.QueryCsvFile(request.FileId, request.UserText);

            return TypedResults.Ok("OK");
        }
    }
}
