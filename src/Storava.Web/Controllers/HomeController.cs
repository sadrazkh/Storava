using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Storava.Web.Models;

namespace Storava.Web.Controllers;

public sealed class HomeController : Controller
{
    [HttpGet("/")]
    public IActionResult Index() => View();

    [HttpGet("/privacy")]
    public IActionResult Privacy() => View();

    [HttpGet("/scan")]
    public IActionResult Scan() => View();

    /// <summary>
    /// The localized error surface, also re-executed by the status-code-pages middleware.
    /// <para>
    /// That re-execution keeps the original request's method, so a form post that is rate limited
    /// or rejected arrives here as a POST. Answering only GET turned every such rejection into an
    /// empty 405 that hid the real status, which is why this accepts both.
    /// </para>
    /// </summary>
    [AcceptVerbs("GET", "POST", Route = "/Home/Error")]
    [IgnoreAntiforgeryToken]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error(int? statusCode = null)
    {
        Response.StatusCode = statusCode is >= 400 and <= 599
            ? statusCode.Value
            : StatusCodes.Status500InternalServerError;

        return View(new ErrorViewModel
        {
            RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier,
            StatusCode = Response.StatusCode
        });
    }
}
