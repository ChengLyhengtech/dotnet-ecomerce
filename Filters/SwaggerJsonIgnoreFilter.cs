using Microsoft.AspNetCore.Http;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Ecomerce_dotnet.Filters
{
    public class SwaggerFileArrayFilter : IOperationFilter
    {
        public void Apply(OpenApiOperation operation, OperationFilterContext context)
        {
            if (operation.RequestBody?.Content == null) return;

            // Target form-data media transmissions exclusively
            var formMediaTypeKey = operation.RequestBody.Content.Keys
                .FirstOrDefault(k => k.Contains("multipart/form-data", StringComparison.OrdinalIgnoreCase));

            if (formMediaTypeKey != null)
            {
                var schema = operation.RequestBody.Content[formMediaTypeKey].Schema;
                if (schema?.Properties != null)
                {
                    // Inspect the DTO class structure for any List<IFormFile> or IFormFile[] properties
                    var formFileFields = context.MethodInfo.GetParameters()
                        .SelectMany(p => p.ParameterType.GetProperties())
                        .Where(prop => prop.PropertyType == typeof(List<IFormFile>) || prop.PropertyType == typeof(IFormFile[]))
                        .Select(prop => prop.Name)
                        .ToList();

                    foreach (var fieldName in formFileFields)
                    {
                        // Look for a matching key in the OpenAPI schema object (handling camelCase matching)
                        var schemaKey = schema.Properties.Keys
                            .FirstOrDefault(k => k.Equals(fieldName, StringComparison.OrdinalIgnoreCase));

                        if (schemaKey != null)
                        {
                            // Override the schema definition to guarantee Swagger UI draws a "Choose File" control array
                            schema.Properties[schemaKey] = new OpenApiSchema
                            {
                                Type = "array",
                                Description = "Upload binary image files directly from your device",
                                Items = new OpenApiSchema
                                {
                                    Type = "string",
                                    Format = "binary"
                                }
                            };
                        }
                    }
                }
            }
        }
    }
}