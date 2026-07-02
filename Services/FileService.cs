using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

public class FileService : IFileService
{
    private readonly IWebHostEnvironment _env;

    public FileService(IWebHostEnvironment env)
    {
        _env = env;
    }

    public async Task<string> SaveFileAsync(IFormFile file, string subFolder)
    {
        if (file == null || file.Length == 0)
            throw new ArgumentException("A valid file asset is required.");

        // Define the destination target folder path dynamically
        string storagePath = Path.Combine(_env.WebRootPath, "uploads", subFolder);
        if (!Directory.Exists(storagePath)) Directory.CreateDirectory(storagePath);

        // Generate a completely unique filename
        var uniqueFileName = $"{Guid.NewGuid()}_{Path.GetFileName(file.FileName)}";
        var completePath = Path.Combine(storagePath, uniqueFileName);

        using (var stream = new FileStream(completePath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        return $"/uploads/{subFolder}/{uniqueFileName}";
    }

    public void DeletePhysicalFile(string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath)) return;

        // Convert the web relative path securely back to the server directory path
        var absolutePath = Path.Combine(_env.WebRootPath, relativePath.TrimStart('/'));

        // No namespace naming collision can happen here!
        if (File.Exists(absolutePath))
        {
            File.Delete(absolutePath);
        }
    }
}