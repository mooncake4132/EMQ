﻿using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Juliet.Model.Param;
using Juliet.Model.Response;
using Juliet.Model.VNDBObject;

namespace Juliet;

public static class Api
{
    private static HttpClient Client { get; } = new()
    {
        BaseAddress = new Uri(Constants.VndbApiUrl),
        // DefaultRequestHeaders = { { "User-Agent", Constants.UserAgent }, }, // getting a CORS error
    };

    private static async Task<T?> Send<T>(HttpRequestMessage req) where T : class
    {
        try
        {
            Console.WriteLine($"Sending request {JsonSerializer.Serialize(req)}"); // TODO
            var res = await Client.SendAsync(req);
            Console.WriteLine("Res: " + JsonSerializer.Serialize(res)); // TODO

            if (res.IsSuccessStatusCode)
            {
                var content = (await res.Content.ReadFromJsonAsync<T>())!;
                return content;
            }
            else
            {
                switch (res.StatusCode)
                {
                    // case HttpStatusCode.BadRequest:
                    //     break;
                    case HttpStatusCode.Unauthorized:
                        // todo auth logic?
                        throw new Exception("Unauthorized.");
                    // case HttpStatusCode.NotFound:
                    //     break;
                    case HttpStatusCode.TooManyRequests:
                        // todo throttling logic?
                        throw new Exception("Throttled.");
                    // case HttpStatusCode.InternalServerError:
                    //     break;
                    // case HttpStatusCode.BadGateway:
                    //     break;
                    default:
                        Console.WriteLine("Error communicating with VNDB. res: " + JsonSerializer.Serialize(res));
                        throw new Exception($"Error communicating with VNDB. Status code: {res.StatusCode}");
                }
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return null;
        }
    }

    public static async Task<Dictionary<string, User>?> GET_user(Param param)
    {
        if (string.IsNullOrWhiteSpace(param.User))
        {
            return null;
        }

        var req = new HttpRequestMessage
        {
            // &fields=lengthvotes,lengthvotes_sum
            RequestUri = new Uri($"user?q={param.User}", UriKind.Relative),
        };

        var res = await Send<Dictionary<string, User>>(req);
        return res;
    }

    public static async Task<ResGET_authinfo?> GET_authinfo(Param param)
    {
        if (string.IsNullOrWhiteSpace(param.APIToken))
        {
            return null;
        }

        var req = new HttpRequestMessage { RequestUri = new Uri("authinfo", UriKind.Relative), };
        req.Headers.Authorization = new AuthenticationHeaderValue("token", param.APIToken);

        var res = await Send<ResGET_authinfo>(req);
        return res;
    }

    public static async Task<List<ResPOST<ResPOST_ulist>>?> POST_ulist(ParamPOST_ulist param)
    {
        // todo validate other params
        if (string.IsNullOrWhiteSpace(param.User))
        {
            return null;
        }

        var final = new List<ResPOST<ResPOST_ulist>>();

        int page = 0;
        bool more;
        do
        {
            page += 1;

            // TODO
            string json = JsonSerializer.Serialize(
                new Dictionary<string, dynamic>()
                {
                    { "user", param.User },
                    { "fields", string.Join(", ", param.Fields.Select(x => x.ToString().ToLowerInvariant())) },
                    { "normalized_filters", param.NormalizedFilters },
                    { "compact_filters", param.CompactFilters },
                    { "results", param.Exhaust ? Constants.MaxResultsPerPage : param.ResultsPerPage },
                    { "page", page },
                    // todo
                    {
                        "filters",
                        new dynamic[]
                        {
                            "or", new dynamic[] { "label", "=", 1 }, new dynamic[] { "label", "=", 2 },
                            new dynamic[] { "label", "=", 7 }
                        } // playing, finished, voted
                    }
                });
            Console.WriteLine("json:" + json);

            var req = new HttpRequestMessage
            {
                RequestUri = new Uri("ulist", UriKind.Relative),
                Method = HttpMethod.Post,
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            if (!string.IsNullOrWhiteSpace(param.APIToken))
            {
                req.Headers.Authorization = new AuthenticationHeaderValue("token", param.APIToken);
            }

            var res = await Send<ResPOST<ResPOST_ulist>>(req);
            if (res != null)
            {
                Console.WriteLine("normalized filters: " + JsonSerializer.Serialize(res.NormalizedFilters));
                // break;

                final.Add(res);
                more = res.More;
            }
            else
            {
                more = false;
            }
        } while (param.Exhaust && more);

        return final;
    }

    // TODO GET_ulist_labels
}