using System;
using System.ComponentModel.DataAnnotations;
using Jellyfin.Api.Constants;
using Jellyfin.Api.Extensions;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.LakeHp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Api.Controllers;

/// <summary>
/// Surround output settings (LakeHP headphone virtualizer / matrix surround) of the calling user and device.
/// </summary>
[Route("LakeHp")]
[Authorize]
[Tags("LakeHp")]
public class LakeHpController : BaseJellyfinApiController
{
    private readonly ILakeHpManager _lakeHp;

    /// <summary>
    /// Initializes a new instance of the <see cref="LakeHpController"/> class.
    /// </summary>
    /// <param name="lakeHp">Instance of the <see cref="ILakeHpManager"/> interface.</param>
    public LakeHpController(ILakeHpManager lakeHp)
    {
        _lakeHp = lakeHp;
    }

    /// <summary>
    /// Gets the surround output settings of the calling user and device, and what the server can use.
    /// </summary>
    /// <response code="200">Settings returned.</response>
    /// <returns>The <see cref="LakeHpStatus"/>.</returns>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<LakeHpStatus> GetStatus()
    {
        return BuildStatus();
    }

    /// <summary>
    /// Changes the calling user's account default and/or the calling device's override.
    /// </summary>
    /// <param name="update">The change.</param>
    /// <response code="200">Settings changed; the new settings are returned.</response>
    /// <returns>The <see cref="LakeHpStatus"/>.</returns>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<LakeHpStatus> UpdateStatus([FromBody, Required] LakeHpStatusUpdate update)
    {
        _lakeHp.UpdatePreferences(User.GetUserId(), User.GetDeviceId(), update);
        return BuildStatus();
    }

    private LakeHpStatus BuildStatus()
    {
        var userId = User.GetUserId();
        var deviceId = User.GetDeviceId();
        var prefs = _lakeHp.GetPreferences(userId);
        var path = _lakeHp.GetFilterPath();

        var deviceMode = LakeHpDeviceMode.Default;
        foreach (var device in prefs.Devices)
        {
            if (string.Equals(device.DeviceId, deviceId, StringComparison.Ordinal))
            {
                deviceMode = device.Mode;
            }
        }

        return new LakeHpStatus
        {
            HeadphonesAvailable = _lakeHp.IsAvailable(LakeHpOutputMode.Headphones),
            MatrixSurroundAvailable = _lakeHp.IsAvailable(LakeHpOutputMode.MatrixSurround),
            FilterFound = path is not null,
            FfmpegSupported = _lakeHp.IsFfmpegSupported(),
            FilterPath = User.IsInRole(UserRoles.Administrator) ? path : null,
            UserMode = prefs.Mode,
            DeviceMode = deviceMode,
            ActiveMode = _lakeHp.GetActiveMode(userId, deviceId)
        };
    }
}
