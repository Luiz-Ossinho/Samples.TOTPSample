# Samples.TOTP
This project is a sample that implents authorization and authentication using TOTP (Timed Once Time Password). 
This is an MFA feature that has been increasing in adoption but many companies still dont use it, even though its a inexpensive, simple and intuitive way to increase security.
It works by generating a cryptographic key that can generate unique codes for each time window as it goes by, and then, the user can register this key in their favorite Authenticator app such as Google Authenticator, Microsoft Authenticator and others.
When the server needs to confirm an users identity and/or access level, it can request the code generated on the Authenticator app and verify its validity.

## Samples
Because the cryptographic key can be generated and fine tuned in many ways, this sample contains two examples: One enabling and using Firebase to generate and validate TOTP, and another generates the key and stores it on a local database.

### Firebase Sample
The firebase sample is in `.\scripts\firebase-totp-sample.ipynb`.
This is a Polyglot Notebook file and to run it you will need Visual Studio Code and [this extension](https://marketplace.visualstudio.com/items?itemName=ms-dotnettools.dotnet-interactive-vscode)

### Local/Blazor sample
The local sample is a .NET 7 Server-Side Blazor project, using Identity and EntityFramework. 
Here are the steps you need to implement your own sample.

#### Scaffolding Identity
Because we will be using and modifying Identity, we need to scaffold its code into our project.

![Scaffolding identity step 1](docs\README\scaffolding-identity1.png)
![Scaffolding identity step 2](docs\README\scaffolding-identity2.png)
Override all and select the default DbContext for the project.
![Scaffolding identity step 3](docs\README\scaffolding-identity3.png)

#### Modifying identity
We will use Claims to keep track of wich users have enabled TOTP MFA, for this a new UserClaimsPrincipalFactory needs to be implemented.

```csharp 
// .\AdditionalUserClaimsPrincipalFactory.cs

namespace WebApp;

public class AdditionalUserClaimsPrincipalFactory :
        UserClaimsPrincipalFactory<IdentityUser, IdentityRole>
{
    public AdditionalUserClaimsPrincipalFactory(
        UserManager<IdentityUser> userManager,
        RoleManager<IdentityRole> roleManager,
        IOptions<IdentityOptions> optionsAccessor)
        : base(userManager, roleManager, optionsAccessor)
    {
    }

    public async override Task<ClaimsPrincipal> CreateAsync(IdentityUser user)
    {
        var principal = await base.CreateAsync(user);
        var identity = (ClaimsIdentity)principal.Identity;

        var claims = new List<Claim>();

        if (user.TwoFactorEnabled)
        {
            claims.Add(new Claim("amr", "mfa"));
        }
        else
        {
            claims.Add(new Claim("amr", "pwd"));
        }

        identity.AddClaims(claims);
        return principal;
    }
}
//...

// .\Progam.cs
// ...
// Remember to configure dependency injection for identity.
var sqliteConnection = new SqliteConnection("DataSource=mylocaldb.db;cache=shared");
sqliteConnection.Open();
builder.Services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(sqliteConnection));

builder.Services.AddIdentity<IdentityUser, IdentityRole>(options => options.SignIn.RequireConfirmedAccount = true)
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services.AddScoped<IUserClaimsPrincipalFactory<IdentityUser>, AdditionalUserClaimsPrincipalFactory>();
builder.Services.AddScoped<AuthenticationStateProvider, RevalidatingIdentityAuthenticationStateProvider<IdentityUser>>();

builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
// ...
using var scope = app.Services.CreateScope();
var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
await context.Database.EnsureCreatedAsync();
```

Some identity's  services need IEmailSender to be injected.
```csharp 
// .\MockEmailSender.cs
public class MockEmailSender : IEmailSender
{
    public static Dictionary<int, HashSet<(string email, string subject, string htmlMessage)>> EmailDictionary { get; set; } = new ();

    public Task SendEmailAsync(string email, string subject, string htmlMessage)
    {
        if (EmailDictionary.TryGetValue(DateTime.Now.Hour, out var existingHashSet))
        {
            existingHashSet.Add((email, subject, htmlMessage));
        }
        else
        {
            var newSingleItemHashset = new HashSet<(string email, string subject, string htmlMessage)>() { (email, subject, htmlMessage) };
            EmailDictionary.Add(DateTime.Now.Hour, newSingleItemHashset);
        }

        return Task.CompletedTask;
    }
}

// Remember to add this to dependecy injection

// .\Progam.cs
// ...
builder.Services.AddSingleton<IEmailSender, MyEmailSender>();
// ...
```


Because we modified Identity�s injection, we need to modify every page in `Areas\Identity\Pages\Account\Manage`
```csharp
// .\Identity/Account/Manage/_Layout.cshtml
@{
    Layout = "/Pages/Shared/_Layout.cshtml";
}
//...

//  Every .cshtml page inside Areas\Identity\Pages\Account\Manage
// ...
@{
    Layout = "_Layout.cshtml";
    // ...
}
// ...
```

We will also add a new policy so we can verify it when needed.
```csharp
// .\Progam.cs

builder.Services.AddAuthorization(options => options.AddPolicy("TwoFactorEnabled", x => x.RequireClaim("amr", "mfa")));
```

#### Protecting features
Now we will protect the FecthData feature in Blazor.
```csharp
@page "/fetchdata"
@using WebApp.Data
@using Microsoft.AspNetCore.Authorization
@using Microsoft.AspNetCore.Identity
@inject WeatherForecastService ForecastService
@inject AuthenticationStateProvider AuthenticationStateProvider
@inject SignInManager<IdentityUser> SignInManager
@inject UserManager<IdentityUser> UserManager
@inject IAuthorizationService AuthorizationService

<PageTitle>Weather forecast</PageTitle>

@if (authorizationResult?.Succeeded ?? false)
{
    <h1>Weather forecast</h1>

    <p>This component demonstrates fetching data from a service.</p>

    @if (forecasts == null)
    {
        <p><em>Loading...</em></p>
    }
    else
    {
        <table class="table">
            <thead>
                <tr>
                    <th>Date</th>
                    <th>Temp. (C)</th>
                    <th>Temp. (F)</th>
                    <th>Summary</th>
                </tr>
            </thead>
            <tbody>
                @foreach (var forecast in forecasts)
                {
                    <tr>
                        <td>@forecast.Date.ToShortDateString()</td>
                        <td>@forecast.TemperatureC</td>
                        <td>@forecast.TemperatureF</td>
                        <td>@forecast.Summary</td>
                    </tr>
                }
            </tbody>
        </table>
    }
}
else
{
    <h1>Weather forecast</h1>

    <p>MFA is NOT enabled.</p>
}


@code {
    private WeatherForecast[]? forecasts;
    private AuthorizationResult? authorizationResult;

    protected override async Task OnInitializedAsync()
    {
        var user = await AuthenticationStateProvider.GetAuthenticationStateAsync();
        authorizationResult = await AuthorizationService.AuthorizeAsync(user?.User!, "TwoFactorEnabled");
        forecasts = await ForecastService.GetForecastAsync(DateOnly.FromDateTime(DateTime.Now));
    }
}
```