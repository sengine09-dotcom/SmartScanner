using SmartScanner.Models;

namespace SmartScanner.Services;

public interface ISentItemsService
{
    void           Add(SentItem item);
    List<SentItem> GetAll();
    void           Delete(int id);
    void           ClearAll();
}
