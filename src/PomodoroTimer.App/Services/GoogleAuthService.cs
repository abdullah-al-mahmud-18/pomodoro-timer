using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Requests;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Drive.v3;
using Google.Apis.Util.Store;
using Serilog;

namespace PomodoroTimer.App.Services;

/// <summary>Thrown when there's no usable stored token and the user has to sign in interactively.</summary>
public class SignInRequiredException : Exception
{
    public SignInRequiredException() : base("Google sign-in is required.") { }
}

/// <summary>
/// OAuth 2.0 installed-app flow (system browser + localhost loopback redirect) with the drive.file scope only.
/// The refresh token lives in &lt;app dir&gt;/google-token and is never logged.
/// </summary>
public class GoogleAuthService
{
    private const string UserKey = "user";
    private static readonly string[] Scopes = { DriveService.Scope.DriveFile };

    private readonly string _clientSecretPath;
    private readonly FileDataStore _tokenStore;

    public GoogleAuthService(string clientSecretPath, string tokenDirectory)
    {
        _clientSecretPath = clientSecretPath;
        _tokenStore = new FileDataStore(tokenDirectory, fullPath: true);
    }

    /// <summary>False when client_secret.json isn't next to the executable — sync is then disabled.</summary>
    public bool IsConfigured => File.Exists(_clientSecretPath);

    /// <summary>Uses the stored token without any browser interaction. Returns null if the user has to sign in.</summary>
    public async Task<UserCredential?> TryGetStoredCredentialAsync(CancellationToken cancellationToken)
    {
        if (await _tokenStore.GetAsync<TokenResponse>(UserKey) is null)
        {
            return null;
        }

        try
        {
            return await GoogleWebAuthorizationBroker.AuthorizeAsync(
                LoadClientSecrets(), Scopes, UserKey, cancellationToken, _tokenStore, new NoInteractionCodeReceiver());
        }
        catch (SignInRequiredException)
        {
            return null;
        }
    }

    /// <summary>
    /// Interactive sign-in. Opens the system browser; <paramref name="onSignInUrl"/> receives the sign-in URL
    /// so it can also be shown to the user in case the browser didn't open.
    /// </summary>
    public async Task<UserCredential> SignInAsync(Action<string> onSignInUrl, CancellationToken cancellationToken)
    {
        await ClearTokenAsync();
        var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
            LoadClientSecrets(), Scopes, UserKey, cancellationToken, _tokenStore, new BrowserCodeReceiver(onSignInUrl));
        Log.Information("Google sign-in completed");
        return credential;
    }

    /// <summary>Deletes the stored token (e.g. after it was revoked or expired).</summary>
    public async Task ClearTokenAsync()
    {
        await _tokenStore.ClearAsync();
        Log.Information("Cleared stored Google token");
    }

    private ClientSecrets LoadClientSecrets()
    {
        using var stream = File.OpenRead(_clientSecretPath);
        return GoogleClientSecrets.FromStream(stream).Secrets;
    }

    /// <summary>Used for silent authorization: if the library asks for a new code, the user has to sign in.</summary>
    private sealed class NoInteractionCodeReceiver : ICodeReceiver
    {
        public string RedirectUri => "http://127.0.0.1/";

        public Task<AuthorizationCodeResponseUrl> ReceiveCodeAsync(AuthorizationCodeRequestUrl url, CancellationToken taskCancellationToken) =>
            throw new SignInRequiredException();
    }

    /// <summary>
    /// Loopback receiver that reports the sign-in URL and keeps waiting even if launching the browser fails
    /// (e.g. no xdg-open on Linux), so the user can open the URL by hand.
    /// </summary>
    private sealed class BrowserCodeReceiver : LocalServerCodeReceiver
    {
        private readonly Action<string> _onSignInUrl;

        public BrowserCodeReceiver(Action<string> onSignInUrl)
        {
            _onSignInUrl = onSignInUrl;
        }

        protected override bool OpenBrowser(string url)
        {
            _onSignInUrl(url);
            try
            {
                if (!base.OpenBrowser(url))
                {
                    Log.Warning("Couldn't open the browser for Google sign-in; the user can open the link shown in the app");
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Couldn't open the browser for Google sign-in; the user can open the link shown in the app");
            }

            return true;
        }
    }
}
