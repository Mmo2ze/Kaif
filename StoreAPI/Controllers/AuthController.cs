using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StoreAPI.Data;
using StoreAPI.Services;
using StoreShared;
using StoreShared.Auth;

namespace StoreAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class AuthController : ControllerBase
{
    private readonly StoreDbContext _db;
    private readonly JwtTokenIssuer _tokens;
    private readonly PasswordHasher<User> _passwordHasher = new();

    public AuthController(StoreDbContext db, JwtTokenIssuer tokens)
    {
        _db = db;
        _tokens = tokens;
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest("Username and password are required.");

        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Username == request.Username, ct);
        if (user is null || !user.IsActive)
            return Unauthorized();

        var verify = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (verify == PasswordVerificationResult.Failed)
            return Unauthorized();

        var (token, expires) = _tokens.CreateToken(user);
        return Ok(new LoginResponse(token, expires));
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<CurrentUserDto>> Me(CancellationToken ct)
    {
        var idClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (idClaim is null || !int.TryParse(idClaim, out var userId))
            return Unauthorized();

        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null || !user.IsActive)
            return Unauthorized();

        return Ok(new CurrentUserDto(user.Id, user.Username, user.Role));
    }

    /// <summary>Cashiers for sales report filter (admin).</summary>
    [Authorize(Roles = nameof(UserRole.Admin))]
    [HttpGet("staff")]
    public async Task<ActionResult<IReadOnlyList<UserListItemDto>>> Staff(CancellationToken ct)
    {
        var rows = await _db.Users.AsNoTracking()
            .Where(u => u.IsActive && (u.Role == UserRole.Seller || u.Role == UserRole.Admin))
            .OrderBy(u => u.Username)
            .Select(u => new UserListItemDto(u.Id, u.Username, u.Role))
            .ToListAsync(ct);
        return Ok(rows);
    }
}
