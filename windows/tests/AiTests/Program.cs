using Lexi;
static void Check(bool ok, string name) { if (!ok) throw new Exception(name); Console.WriteLine("PASS " + name); }
Check(!AiService.ParseExpansion("{}", ["phrases"]).HasContent, "phrases omitted is valid");
Check(!AiService.ParseExpansion("{\"phrases\":[]}", ["phrases"]).HasContent, "empty phrases valid");
var json = """{"phrases":[{"en":"take off","zh":"起飞"},{"en":"take care of","zh":"照顾"}]}""";
var result = AiService.ParseExpansion(json, ["phrases"]);
Check(result.Phrases.Count == 2 && result.HasContent && result.Phrases[0].Chinese == "起飞", "bilingual phrases parsed");
Check(!AiService.ParseExpansion(json, []).HasContent, "unchecked phrases ignored");
foreach (var bad in new[] { """{"phrases":[{"en":"take off","zh":""},{"en":"take care","zh":"保重"}]}""", """{"phrases":[{"en":"take off","zh":"起飞"}]}""", """{"phrases":[{"en":"take off","zh":"起飞"},{"en":"TAKE OFF","zh":"起飞"}]}""", """{"phrases":"invalid"}""" })
{
    var rejected = false;
    try { AiService.ParseExpansion(bad, ["phrases"]); } catch (InvalidOperationException) { rejected = true; }
    Check(rejected, "malformed phrases rejected");
}

using (var listener = new System.Net.HttpListener())
{
    var portFinder = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
    portFinder.Start(); var port = ((System.Net.IPEndPoint)portFinder.LocalEndpoint).Port; portFinder.Stop();
    listener.Prefixes.Add($"http://127.0.0.1:{port}/"); listener.Start();
    var request = new AiService().GenerateExpansionAsync("take", ["phrases"], new AppSettings { BaseUrl = $"http://127.0.0.1:{port}", ApiKey = "test-only", Model = "test-only" });
    var context = await listener.GetContextAsync();
    using var reader = new StreamReader(context.Request.InputStream);
    using var body = System.Text.Json.JsonDocument.Parse(await reader.ReadToEndAsync());
    var prompt = body.RootElement.GetProperty("messages")[0].GetProperty("content").GetString()!;
    Check(prompt.Contains("切勿生硬编造拼凑") && prompt.Contains("省略") && prompt.Contains("phrases:"), "outgoing prompt enforces optional idiomatic phrases");
    Check(!prompt.Contains("examples:") && !body.RootElement.TryGetProperty("max_tokens", out _), "outgoing request only includes selected schema without token cap");
    var response = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new { choices = new[] { new { message = new { content = "{}" } } } });
    await context.Response.OutputStream.WriteAsync(response); context.Response.Close();
    Check(!(await request).HasContent, "omitted phrases accepted end to end over HTTP");
}
