using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Azure;
using Azure.AI.OpenAI;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SearchThingy;

public class DoSearch
{
    private readonly IConfiguration _configuration;

    public DoSearch(IConfiguration configuration)
    {
        _configuration = configuration;
    }
    
    [FunctionName("DoSearch")]
    public async Task<IActionResult> RunAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequest req, ILogger log)
    {
        var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
        dynamic data = JsonConvert.DeserializeObject(requestBody);
        string question = data?.question;
        List<string> answers = data?.answers.ToObject<List<string>>();
        
        Uri openAiEndpoint = new Uri(_configuration["OpenAiEndpoint"]);
        string openAiKey = _configuration["OpenAiKey"];
        AzureKeyCredential openAiCredential = new(openAiKey);
        OpenAIClient openAiClient = new OpenAIClient(openAiEndpoint, openAiCredential);
        
        Uri searchEndpoint = new(_configuration["SearchEndpoint"]);
        string searchKey = _configuration["SearchKey"];
        AzureKeyCredential searchCredentials = new(searchKey);
        SearchClient searchClient = new SearchClient(searchEndpoint, "functions", searchCredentials);
        
        EmbeddingsOptions embeddingsOptions = new(question);

        Embeddings embeddings = await openAiClient.GetEmbeddingsAsync("text-embedding-ada-002", embeddingsOptions);
        IReadOnlyList<float> contentVector = embeddings.Data[0].Embedding;
        
        SearchResults<Thingy> response = await searchClient.SearchAsync<Thingy>(null,
            new SearchOptions
            {
                Vectors = { new() { Value = contentVector, KNearestNeighborsCount = 3, Fields = { "contentVector" } } },
            });

        int count = 0;

        var results = response.GetResultsAsync();
        
        
        StringBuilder prompt = new StringBuilder();
        prompt.AppendLine("As a Microsoft Azure certification instructor, you will be presented with a multiple-choice question that includes answer options. You will also receive three documentation snippets relevant to the question. Use these snippets to identify the correct answer. If the correct option is not evident from the snippets, you may browse the internet for additional information. Your output should be the selection of the correct option provided within the question.");
        await foreach (var result in results)
        {
            count++;
            var doc = result.Document;

            prompt.AppendLine($"Snippet {count}:");
            prompt.AppendLine(doc.content);
        }
        
        if (answers != null)
        {
            prompt.AppendLine("Options:");
            foreach (var answer in answers)
            {
                prompt.AppendLine($"Option: {answer}");
            }
        }
        
        Response<ChatCompletions> responseWithoutStream = await openAiClient.GetChatCompletionsAsync(
            "gpt-4",
            new ChatCompletionsOptions
            {
                Messages =
                {
                    new ChatMessage(ChatRole.System, prompt.ToString()),
                    new ChatMessage(ChatRole.User, question)
                },
                Temperature = (float) 0.5,
                MaxTokens = 3000,
                NucleusSamplingFactor = (float) 0.95,
                FrequencyPenalty = 0,
                PresencePenalty = 0
            });
        
        ChatCompletions completions = responseWithoutStream.Value;
        var rawScript = completions.Choices[0].Message.Content;
        
        return new OkObjectResult(rawScript);
    }
}