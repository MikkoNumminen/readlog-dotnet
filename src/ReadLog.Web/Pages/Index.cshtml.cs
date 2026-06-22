using Microsoft.AspNetCore.Mvc.RazorPages;
using ReadLog.Web.Dtos;
using ReadLog.Web.Services;

namespace ReadLog.Web.Pages;

public class IndexModel : PageModel
{
    private readonly IReadLogService _readLog;

    public IndexModel(IReadLogService readLog)
    {
        _readLog = readLog;
    }

    public IReadOnlyList<PublicReadDto> RecentReads { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        RecentReads = await _readLog.GetRecentPublicReadsAsync(cancellationToken);
    }
}
