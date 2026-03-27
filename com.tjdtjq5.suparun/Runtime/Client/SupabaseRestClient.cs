using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace Tjdtjq5.SupaRun
{
    /// <summary>Supabase PostgREST 직접 조회. [Config] 타입 전용.</summary>
    class SupabaseRestClient
    {
        readonly string _restUrl; // https://xxx.supabase.co/rest/v1
        readonly string _anonKey;

        public SupabaseRestClient(string supabaseUrl, string anonKey)
        {
            _restUrl = supabaseUrl?.TrimEnd('/') + "/rest/v1";
            _anonKey = anonKey;
        }

        static string ToSnakeCase(string name)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < name.Length; i++)
            {
                if (char.IsUpper(name[i]) && i > 0) sb.Append('_');
                sb.Append(char.ToLower(name[i]));
            }
            return sb.ToString();
        }

        public async Task<ServerResponse<T>> Get<T>(object id)
        {
            var table = ToSnakeCase(typeof(T).Name);
            var url = $"{_restUrl}/{table}?id=eq.{id}&limit=1";

            var list = await Fetch<List<T>>(url);
            if (!list.success)
                return new ServerResponse<T> { success = false, error = list.error, errorType = list.errorType, statusCode = list.statusCode };

            var data = list.data != null && list.data.Count > 0 ? list.data[0] : default;
            return new ServerResponse<T>
            {
                success = data != null,
                data = data,
                statusCode = data != null ? 200 : 404,
                errorType = data != null ? ErrorType.None : ErrorType.NotFound,
                error = data != null ? null : $"{typeof(T).Name} not found: {id}"
            };
        }

        public async Task<ServerResponse<List<T>>> GetAll<T>()
        {
            var table = ToSnakeCase(typeof(T).Name);
            var url = $"{_restUrl}/{table}";
            return await Fetch<List<T>>(url);
        }

        async Task<ServerResponse<T>> Fetch<T>(string url)
        {
            try
            {
                using var request = new UnityWebRequest(url, "GET");
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("apikey", _anonKey);
                request.SetRequestHeader("Authorization", $"Bearer {_anonKey}");
                request.timeout = 15;

                var op = request.SendWebRequest();
                while (!op.isDone) await Task.Yield();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    return new ServerResponse<T>
                    {
                        success = false,
                        error = request.error,
                        statusCode = (int)request.responseCode,
                        errorType = request.responseCode >= 500 ? ErrorType.ServerError : ErrorType.BadRequest
                    };
                }

                var data = JsonConvert.DeserializeObject<T>(request.downloadHandler.text);
                return new ServerResponse<T> { success = true, data = data, statusCode = 200 };
            }
            catch (Exception ex)
            {
                return new ServerResponse<T> { success = false, error = ex.Message, errorType = ErrorType.NetworkError };
            }
        }
    }
}
