using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StoreAPI.Data;
using StoreShared;
using StoreShared.Auth;

namespace StoreAPI.Controllers;

[ApiController]
[Route("api/users")]
[Authorize(Roles = nameof(UserRole.Admin))]
public sealed class UsersController : ControllerBase
{
    private readonly StoreDbContext _db;
    private readonly PasswordHasher<User> _passwordHasher = new();

    public UsersController(StoreDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<UserAdminRowDto>>> List(CancellationToken ct)
    {
        var rows = await _db.Users.AsNoTracking()
            .OrderBy(u => u.Username)
            .Select(u => new UserAdminRowDto(u.Id, u.Username, u.Role, u.IsActive))
            .ToListAsync(ct);
        return Ok(rows);
    }

    [HttpPost]
    public async Task<ActionResult<UserAdminRowDto>> Create([FromBody] CreateUserRequest body, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.Username))
            return BadRequest("Username is required.");
        if (string.IsNullOrWhiteSpace(body.Password))
            return BadRequest("Password is required.");
        if (body.Role != UserRole.Admin && body.Role != UserRole.Seller)
            return BadRequest("Role must be Admin or Seller.");

        var username = body.Username.Trim();
        if (await _db.Users.AnyAsync(u => u.Username == username, ct))
            return Conflict("Username already exists.");

        var user = new User
        {
            Username = username,
            Role = body.Role,
            IsActive = true,
        };
        user.PasswordHash = _passwordHasher.HashPassword(user, body.Password);
        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);

        return Ok(new UserAdminRowDto(user.Id, user.Username, user.Role, user.IsActive));
    }

    [HttpPut("{id:int}/password")]
    public async Task<IActionResult> ResetPassword(int id, [FromBody] ResetPasswordRequest body, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.NewPassword))
            return BadRequest("New password is required.");

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
            return NotFound();

        user.PasswordHash = _passwordHasher.HashPassword(user, body.NewPassword);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPut("{id:int}/deactivate")]
    public async Task<IActionResult> Deactivate(int id, CancellationToken ct)
    {
        var idClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (idClaim is null || !int.TryParse(idClaim, out var adminId))
            return Unauthorized();

        if (id == adminId)
            return BadRequest("You cannot deactivate your own account.");

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
            return NotFound();

        if (!user.IsActive)
            return NoContent();

        user.IsActive = false;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }
}
