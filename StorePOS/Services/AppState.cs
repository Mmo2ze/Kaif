namespace StorePOS.Services;

public sealed class AppState
{
    public string? Token { get; private set; }
    public string Username { get; private set; } = "";
    public string Role { get; private set; } = "";
    public int UserId { get; private set; }
    public string? ServerWarning { get; private set; }
    public bool IsAuthenticated => !string.IsNullOrEmpty(Token);

    public event Action? Changed;

    public void SetUser(string token, string username, string role, int userId = 0)
    {
        Token = token;
        Username = username;
        Role = role;
        UserId = userId;
        Changed?.Invoke();
    }

    public void Logout()
    {
        Token = null;
        Username = "";
        Role = "";
        UserId = 0;
        ServerWarning = null;
        Changed?.Invoke();
    }

    public void SetServerWarning(string? message)
    {
        ServerWarning = message;
        Changed?.Invoke();
    }
}
