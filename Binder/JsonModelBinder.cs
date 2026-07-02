using Microsoft.AspNetCore.Mvc.ModelBinding;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Ecomerce_dotnet.Binder
{
    public class JsonModelBinder : IModelBinder
    {
        public Task BindModelAsync(ModelBindingContext bindingContext)
        {
            if (bindingContext == null) throw new ArgumentNullException(nameof(bindingContext));

            // 1. Try to read from the standard Form data provider first
            var valueProviderResult = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
            IReadOnlyList<string> rawValues = valueProviderResult.Values;

            // 2. CRITICAL FALLBACK: If empty, explicitly look at the URL Query String parameters 
            // to catch Swagger UI's query-string placement behavior!
            if (rawValues.Count == 0)
            {
                var query = bindingContext.ActionContext.HttpContext.Request.Query;
                if (query.TryGetValue(bindingContext.ModelName, out var queryValues))
                {
                    rawValues = queryValues;
                }
            }

            // If absolutely nothing is found in form body or query string, exit early
            if (rawValues.Count == 0) return Task.CompletedTask;

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            // Determine the underlying T type inside List<T>
            var listType = bindingContext.ModelType.GenericTypeArguments[0];
            var listInstance = Activator.CreateInstance(typeof(List<>).MakeGenericType(listType)) as System.Collections.IList;

            foreach (var rawValue in rawValues)
            {
                if (string.IsNullOrWhiteSpace(rawValue)) continue;
                var trimmed = rawValue.Trim();

                try
                {
                    // Handle if passed as a single stringified JSON array: [{"color":"red"}]
                    if (trimmed.StartsWith("["))
                    {
                        var deserializedList = JsonSerializer.Deserialize(rawValue, bindingContext.ModelType, options) as System.Collections.IList;
                        if (deserializedList != null)
                        {
                            foreach (var item in deserializedList)
                            {
                                listInstance?.Add(item);
                            }
                        }
                    }
                    // Handle if passed as individual object parameters (like Swagger UI does here)
                    else
                    {
                        var item = JsonSerializer.Deserialize(rawValue, listType, options);
                        if (item != null) listInstance?.Add(item);
                    }
                }
                catch
                {
                    bindingContext.ModelState.TryAddModelError(bindingContext.ModelName, $"Failed to parse JSON string: {rawValue}");
                    return Task.CompletedTask;
                }
            }

            bindingContext.Result = ModelBindingResult.Success(listInstance);
            return Task.CompletedTask;
        }
    }
}