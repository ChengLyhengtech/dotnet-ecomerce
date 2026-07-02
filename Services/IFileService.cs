using Microsoft.AspNetCore.Http;

public interface IFileService
{
    Task<string> SaveFileAsync(IFormFile file, string subFolder);
    void DeletePhysicalFile(string relativePath);
}