using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Storava.Contracts.Agent;
using Storava.Web.Data;
using Storava.Web.Models;
using Storava.Web.Services;

namespace Storava.Web.Controllers;

/// <summary>
/// What the Storava page asks this server about the user's own Agents.
/// <para>
/// It answers two questions and no more: which devices are paired, and may this page talk to one
/// of them right now. Everything the page does with an Agent after that happens over loopback,
/// between the two of them, and is never described here.
/// </para>
/// </summary>
[ApiController]
[Authorize]
[Route("api/account/devices")]
public sealed class DevicesApiController(
    UserManager<ApplicationUser> userManager,
    IDevicePairingService pairing) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<BrowserDeviceViewModel>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();

        var devices = await pairing.ListAsync(user.Id, cancellationToken);

        return Ok(devices
            .Select(device => new BrowserDeviceViewModel(device.Id, device.DisplayName, device.LastSeenAtUtc))
            .ToList());
    }

    /// <summary>
    /// Mints a pass for one Agent, bound to this site's origin and good for a few minutes.
    /// <para>
    /// POST rather than GET because it produces a credential: a GET would land in browser history,
    /// in proxy logs and in a bookmark. The global antiforgery filter covers it, and the page sends
    /// the token header it already has.
    /// </para>
    /// </summary>
    [HttpPost("{deviceId:guid}/access-token")]
    [EnableRateLimiting("account")]
    [ProducesResponseType<AgentAccessTokenViewModel>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AccessToken(Guid deviceId, CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();

        // The origin comes from the request this server is serving, never from the caller's body:
        // a page must not be able to ask for a token that works somewhere else.
        string origin = $"{Request.Scheme}://{Request.Host.Value}";

        var issued = await pairing.IssueAccessTokenAsync(user.Id, deviceId, origin, cancellationToken);
        if (issued is null)
        {
            // One answer for "not yours", "removed" and "unreadable": the page can do nothing
            // different about any of them, and distinguishing them would confirm ids to a prober.
            return NotFound();
        }

        return Ok(new AgentAccessTokenViewModel(
            issued.Token,
            issued.ExpiresAtUtc,
            AgentEndpoints.Ports,
            AgentEndpoints.ProtocolVersion));
    }
}
