using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using OllamaSharp;
using System.Collections.Concurrent;

/**https://learn.microsoft.com/en-us/dotnet/ai/quickstarts/build-mcp-client**/
// v1: Ollama local without UseFunctionInvocation
//IChatClient client =
//    new OllamaApiClient(new Uri("http://10.0.4.3:11434/"), "qwen2.5:32b");

// v2: Ollama local with UseFunctionInvocation
//IChatClient client = new ChatClientBuilder(new OllamaChatClient(new Uri("http://10.0.4.3:11434"), "qwen2.5:32b"))
//                .UseFunctionInvocation()
//                .Build();

// v3: Ollama local with UseFunctionInvocation parameters
IChatClient client = new ChatClientBuilder(new OllamaApiClient(new Uri("http://10.0.4.3:11434"), "qwen2.5:32b"))
    .UseFunctionInvocation(
        configure: cfg =>
        {
          cfg.MaximumIterationsPerRequest = 10;
          cfg.AllowConcurrentInvocation = false;
        })
    .Build();

// brave search
bool isWindows = OperatingSystem.IsWindows();
string command = isWindows ? "cmd.exe" : "/bin/sh";
string[] arguments = isWindows
    ? new[] { "/C", "set BRAVE_API_KEY=xxxxxx && npx -y @modelcontextprotocol/server-brave-search" }
    : new[] { "-c", "BRAVE_API_KEY=xxxxxx npx -y @modelcontextprotocol/server-brave-search" };

IMcpClient mcpClient = await McpClientFactory.CreateAsync(
    new StdioClientTransport(new StdioClientTransportOptions()
    {
      Command = command,
      Arguments = arguments,
      Name = "MCP Server Brave Search",
    }));

// List all available tools from the MCP server.
Console.WriteLine("Available tools:");
IList<McpClientTool> tools = await mcpClient.ListToolsAsync();
foreach (McpClientTool tool in tools)
{
  Console.WriteLine($"{tool}");
}
Console.WriteLine();

// Conversational loop that can utilize the tools via prompts.
/*Question: I want to travel to China for seven days, and need to book a airplane from Melbourne in 3 days if the weather is good, after I arrive at China, I need to book a hotel*/
List<ChatMessage> messages = [];
while (true)
{
  Console.Write("Prompt (type 'stats' to see statistics, 'quit' to exit): ");
  var input = Console.ReadLine();
  
  if (input?.ToLower() == "quit") break;
  if (input?.ToLower() == "stats")
  {
    ToolStatistics.DisplayToolStatistics();
    continue;
  }
  
  messages.Add(new(ChatRole.User, input));

  List<ChatResponseUpdate> updates = [];
  var responseStartTime = DateTime.Now;
  
  await foreach (ChatResponseUpdate update in client
      .GetStreamingResponseAsync(messages, new() { Tools = [.. tools] }))
  {
    Console.Write(update.Text);
    updates.Add(update);
  }
  
  var responseEndTime = DateTime.Now;
  var responseDuration = responseEndTime - responseStartTime;
  
  // Log response statistics
  ToolStatistics.LogResponse(responseDuration);
  
  Console.WriteLine();
  messages.AddMessages(updates);
}

// Display final statistics before exit
ToolStatistics.DisplayToolStatistics();

// Tool statistics helper class - must be placed after top-level statements
public static class ToolStatistics
{
  private static readonly ConcurrentDictionary<string, int> ToolAvailableCounts = new();
  private static readonly ConcurrentDictionary<string, List<DateTime>> ResponseTimes = new();
  private static readonly List<TimeSpan> ResponseDurations = [];
  private static readonly object lockObject = new();

  // Log response
  public static void LogResponse(TimeSpan duration)
  {
    lock (lockObject)
    {
      ResponseDurations.Add(duration);
      ResponseTimes.AddOrUpdate("responses", [DateTime.Now], (key, value) => { value.Add(DateTime.Now); return value; });
    }
    
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Response completed in {duration.TotalMilliseconds:F0}ms");
  }

  // Display statistics
  public static void DisplayToolStatistics()
  {
    Console.WriteLine("\n=== Tool Usage Statistics ===");
    
    Console.WriteLine("Available Tools:");
    foreach (var kvp in ToolAvailableCounts)
    {
      Console.WriteLine($"  - {kvp.Key}");
    }
    
    lock (lockObject)
    {
      if (ResponseDurations.Count > 0)
      {
        var avgDuration = ResponseDurations.Average(d => d.TotalMilliseconds);
        var minDuration = ResponseDurations.Min(d => d.TotalMilliseconds);
        var maxDuration = ResponseDurations.Max(d => d.TotalMilliseconds);
        
        Console.WriteLine($"\nResponse Statistics:");
        Console.WriteLine($"  Total responses: {ResponseDurations.Count}");
        Console.WriteLine($"  Average duration: {avgDuration:F0}ms");
        Console.WriteLine($"  Min duration: {minDuration:F0}ms");
        Console.WriteLine($"  Max duration: {maxDuration:F0}ms");
      }
    }
    
    if (ResponseTimes.ContainsKey("responses"))
    {
      var times = ResponseTimes["responses"];
      if (times.Count > 0)
      {
        Console.WriteLine($"  First response: {times.Min():yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine($"  Last response: {times.Max():yyyy-MM-dd HH:mm:ss}");
      }
    }
    
    Console.WriteLine();
  }
}

/* Test UseFunctionInvocation with 10 maxmium iteration
 Available tools:
brave_web_search
brave_local_search

Prompt (type 'stats' to see statistics, 'quit' to exit): I want to travel to China for seven days, and need to book a airplane from Melbourne in 3 days if the weather is good, after I arrive at China, I need to book a hotel
To assist you with your travel plans, we will first check flight options from Melbourne to China departing in 3 days. Since the decision depends on the weather, let's also look for general information about booking hotels in China.

For now, let's start by searching for flights and then we can move onto finding hotel options based on your preferences upon arrival.

Let me find some flights first.
I found several options for flights from Melbourne to China departing in the next 3 days. Here's a summary of some available services:

- **Singapore Airlines**: Offers travel experiences with world-class service.
- **Skyscanner**: Provides cheap flight deals, starting at $487.
- **China Airlines/Mandarin Airlines**: Known for their best experience and fares.
- **Flight Centre**: Search for the cheapest flight deals from Melbourne to China.
- **momondo**: Compares prices across multiple airlines and travel sites to find the cheapest flights.

The specific prices are not available directly but can be explored through these links:

1. [Singapore Airlines](https://www.singaporeair.com/en-au/flights-from-melbourne-to-china)
2. [Skyscanner](https://www.skyscanner.com.au/routes/mela/cn/melbourne-to-china.html)
3. [China Airlines/Mandarin Airlines](https://www.china-airlines.com/en-au/flights-from-melbourne-to-china)
4. [Flight Centre](https://www.flightcentre.com.au/flights/au-vic-melbourne/cn)
5. [momondo](https://www.momondo.com.au/flights/melbourne/china)

Once you have chosen your flight, we can proceed to find hotel options in China based on the specific city or location where you will be staying.

Would you like to move forward with booking a hotel now? If so, please specify the preferred city and any other preferences such as price range, amenities, etc.[17:53:17] Response completed in 16329ms

Prompt (type 'stats' to see statistics, 'quit' to exit): yes
Great! Now that we have some flight options, let's proceed with finding hotel accommodations in China based on your preferences. Could you please specify the city or location within China where you plan to stay? Additionally, any specific requirements such as price range, amenities, or type of hotel would be helpful.[17:53:28] Response completed in 3745ms

Prompt (type 'stats' to see statistics, 'quit' to exit): Shanghai, no other requirements
Here are some of the best hotels in Shanghai based on various traveler reviews and recommendations:

1. **The Peninsula Shanghai**
   - A luxurious hotel fronting the Bund with spectacular views of the Huangpu River, Pudong, and its own gardens.
   - [Website](https://www.peninsula.com/en/shanghai/5-star-luxury-hotel-bund)

2. **Shangri-La Hotel, Shanghai Jingan**
   - Located in the heart of Shanghai's commercial district, offering a blend of luxury and convenience.
   - Mentioned as one of the better options for luxury stays.

3. **Park Hyatt Pudong**
   - Features a bar on the 90th floor with great views of the city.
   - [Website](https://www.tripadvisor.com/Hotel_Review-g308272-d1546625-Reviews-Park_Hyatt_Shanghai-Shanghai.html)

4. **Ritz Carlton Shanghai IFC**
   - Part of the International Finance Centre (IFC) and known for its luxury and services.
   - [Website](https://www.tripadvisor.com/Hotel_Review-g308272-d1574655-Reviews-Ritz_Carlton_Shanghai_IFC-Shanghai.html)

5. **Capella Shanghai**
   - A modern, luxurious option in the city that blends traditional and contemporary elements.
   - [Website](https://www.capellahotels.com/shanghai/)

These hotels are highly regarded for their luxury and amenities. You can explore more details on their websites or check prices and availability through platforms like Tripadvisor.

Would you like to look into any specific hotel further, or is there anything else I can assist you with?[17:54:34] Response completed in 14552ms

Prompt (type 'stats' to see statistics, 'quit' to exit): no
Great! It looks like you have some top options for luxury hotels in Shanghai. Here are a few highlights based on the information gathered:

1. **The Peninsula Shanghai**
   - Fronts the Bund with spectacular views of the Huangpu River and Pudong.
   - Blends historic architecture with Art Deco-inspired glamour.
   - [Website](https://www.peninsula.com/en/shanghai/5-star-luxury-hotel-bund)

2. **Shangri-La Hotel, Shanghai Jingan**
   - Located in the heart of Shanghai's commercial district.
   - Highly rated for luxury and convenience.

3. **Park Hyatt Pudong**
   - Features a bar on the 90th floor with great views of the city.
   - [Website](https://www.tripadvisor.com/Hotel_Review-g308272-d1546625-Reviews-Park_Hyatt_Shanghai-Shanghai.html)

4. **Ritz Carlton Shanghai IFC**
   - Part of the International Finance Centre (IFC) and known for its luxury and services.
   - [Website](https://www.tripadvisor.com/Hotel_Review-g308272-d1574655-Reviews-Ritz_Carlton_Shanghai_IFC-Shanghai.html)

5. **Capella Shanghai**
   - A modern, luxurious option in the city that blends traditional and contemporary elements.
   - [Website](https://www.capellahotels.com/shanghai/)

If you don't have any further requirements or need more information, these options should provide a great starting point for your stay in Shanghai. Enjoy your trip! If there's anything else I can help with, feel free to ask.[17:54:52] Response completed in 12968ms

Prompt (type 'stats' to see statistics, 'quit' to exit):
 */

/* Test UseFunctionInvocation with 2 maxmium iteration
 Available tools:
brave_web_search
brave_local_search

Prompt (type 'stats' to see statistics, 'quit' to exit):  I want to travel to Shanghai for seven days, and need to book a airplane from Melbourne in 7 days if the weather is good, otherwise, book a flight the day after that day whatever the weather, after I arrive at China, I need to book a hotel. Decide by yourself and just give me the result. Confirm when it reminds.
Based on your request, let's assume the weather forecast for Melbourne in 7 days is good; thus, we will proceed with booking a flight from Melbourne to Shanghai seven days from now. If the weather were not to be good, we would book the flight for the following day.

**Flight Booking:**
- **Departure Date:** In 7 days (assuming good weather) or in 8 days if the weather is not good.
- **Origin:** Melbourne
- **Destination:** Shanghai

Since I cannot actually make a booking or check real-time weather data, we'll proceed hypothetically. After securing the flight, it's time to look for hotel accommodations in Shanghai.

Given that you're looking to stay for seven days, let's find some suitable hotels in Shanghai. We can use a search tool to gather information on available options. However, since I cannot directly book flights or hotels, I will suggest searching for these services based on the given dates and your preferences.

Now, let's search for hotel options in Shanghai.
Based on the search results, here are some of the best hotels in Shanghai that you might consider for your seven-day stay:

1. **THE 10 BEST Hotels in Shanghai**:
   - [Tripadvisor](https://www.tripadvisor.com/Hotels-g308272-Shanghai-Hotels.html)
     - Top-rated hotels like The Middle House Shanghai, InterContinental Shanghai Pudong, and Fairmont Peace Hotel On The Bund have been mentioned with excellent reviews from travelers.

2. **Top 10 Hotels in Shanghai**:
   - [Hotels.com](https://www.hotels.com/de220308/hotels-shanghai-china/)
     - Flexible booking options available for over 2,400 hotels in Shanghai based on real guest reviews.

3. **Luxury Hotels in Shanghai**:
   - [Booking.com](https://www.booking.com/luxury/city/cn/shanghai.html)
   - [Forbes Travel Guide](https://www.forbestravelguide.com/destinations/shanghai-china)

These resources provide a range of options from budget-friendly to luxurious accommodations. For your trip starting seven days from now (assuming good weather) or eight days if the weather is not good, I recommend checking out these websites for further details and booking based on your preferences.

Would you like more specific recommendations or any other assistance?[17:59:58] Response completed in 18541ms

Prompt (type 'stats' to see statistics, 'quit' to exit):
 */