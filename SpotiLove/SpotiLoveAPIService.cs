using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

namespace SpotiLove
{
    class SpotiLoveAPIService
    {
        static public async Task<List<UserDto>?> GetSwipes(UserDto current)
        {
            try
            {
                Debug.WriteLine($"=== GetSwipes called ===");
                Debug.WriteLine($"Current user ID: {current?.Id}");
                Debug.WriteLine($"Current user Name: {current?.Name}");

                if (current == null || current.Id == Guid.Empty)
                {
                    Debug.WriteLine($" Current user is null or has empty ID!");
                    return null;
                }

                if (current.MusicProfile == null)
                {
                    Debug.WriteLine($"Warning: User {current.Id} has no MusicProfile");
                }

                using var client = new HttpClient();
                client.BaseAddress = new Uri("https://spotilove.danielnaz.com");
                client.Timeout = TimeSpan.FromSeconds(30);

                var url = $"users?userId={current.Id}&count=10";
                Debug.WriteLine($" Making request to: {client.BaseAddress}{url}");

                var response = await client.GetAsync(url);
                Debug.WriteLine($" Response Status: {response.StatusCode}");

                var responseBody = await response.Content.ReadAsStringAsync();
                Debug.WriteLine($" Response Body Length: {responseBody?.Length ?? 0}");

                if (responseBody != null && responseBody.Length < 500)
                {
                    Debug.WriteLine($" Full Response: {responseBody}");
                }
                else
                {
                    Debug.WriteLine($" Response Preview: {responseBody?.Substring(0, Math.Min(500, responseBody?.Length ?? 0))}...");
                }

                if (!response.IsSuccessStatusCode)
                {
                    Debug.WriteLine($" API Error: {response.StatusCode}");
                    Debug.WriteLine($" Full Error Response: {responseBody}");
                    return null;
                }

                var result = JsonSerializer.Deserialize<TakeExUsersResponse>(
                    responseBody,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                if (result == null)
                {
                    Debug.WriteLine(" Deserialization returned null");
                    return null;
                }

                Debug.WriteLine($" Success: {result.Success}");
                Debug.WriteLine($"   Count: {result.Count}");
                Debug.WriteLine($"   Message: {result.Message}");

                if (result.Success && result.Users != null && result.Users.Count > 0)
                {
                    Debug.WriteLine($" Retrieved {result.Users.Count} users");
                    foreach (var user in result.Users.Take(3))
                    {
                        Debug.WriteLine($"   - User {user.Id}: {user.Name}, Age {user.Age}");
                    }
                    return result.Users;
                }
                else
                {
                    Debug.WriteLine($"{result.Message}");
                    return new List<UserDto>();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($" Error: {ex.Message}");
                Debug.WriteLine($"Stack: {ex.StackTrace}");
                return null;
            }
        }
    }
}