using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using StoreAPI.Options;
using StoreAPI.Services;
using StoreShared;
using StoreShared.Pos;

namespace StoreAPI.Controllers;

[ApiController]
[Route("api/pos")]
public sealed class PosController : ControllerBase
{
    private readonly PosOptions _options;
    private readonly IMemoryCache _cache;
    private readonly StoreRuntimeSettings _storeSettings;

    public PosController(IOptions<PosOptions> options, IMemoryCache cache, StoreRuntimeSettings storeSettings)
    {
        _options = options.Value;
        _cache = cache;
        _storeSettings = storeSettings;
    }

    /// <summary>Sellers call this to obtain a one-time discount token when seller discounts are disabled.</summary>
    [HttpPost("authorize-discount")]
    [Authorize(Roles = nameof(UserRole.Seller))]
    public ActionResult<AuthorizeDiscountResponse> AuthorizeDiscount([FromBody] AuthorizeDiscountRequest body)
    {
        if (_storeSettings.AllowSellerDiscount)
            return Ok(new AuthorizeDiscountResponse(null));

        if (string.IsNullOrWhiteSpace(body.Pin) || body.Pin != _options.AdminDiscountPin)
            return Unauthorized();

        var idClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (idClaim is null || !int.TryParse(idClaim, out var userId))
            return Unauthorized();

        var token = Guid.NewGuid().ToString("N");
        _cache.Set($"discount-auth:{token}", userId, TimeSpan.FromMinutes(2));
        return Ok(new AuthorizeDiscountResponse(token));
    }
}
