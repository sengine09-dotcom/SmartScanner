using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace SmartScanner.Services;

public class PdfService : IPdfService
{
    static PdfService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public Task<string> CreatePdfAsync(List<byte[]> imagePages, string outputPath)
    {
        return Task.Run(() =>
        {
            Document.Create(container =>
            {
                foreach (var imageData in imagePages)
                {
                    container.Page(page =>
                    {
                        page.Size(PageSizes.A4);
                        page.Margin(0);
                        page.Content().Image(imageData).FitArea();
                    });
                }
            }).GeneratePdf(outputPath);

            return outputPath;
        });
    }
}
