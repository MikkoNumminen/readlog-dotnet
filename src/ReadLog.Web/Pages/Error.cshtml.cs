using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ReadLog.Web.Pages;

[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[IgnoreAntiforgeryToken]
public class ErrorModel : PageModel
{
    public string? RequestId { get; set; }

    public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);

    /// <summary>Set when the page is re-executed for a status code (e.g. 404).</summary>
    public int? StatusCodeValue { get; set; }

    public void OnGet(int? code = null)
    {
        StatusCodeValue = code;
        RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
    }
}
