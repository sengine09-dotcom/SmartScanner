namespace SmartScanner.Services;

public interface IPdfService
{
    Task<string> CreatePdfAsync(List<byte[]> imagePages, string outputPath);
}
