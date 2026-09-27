using NameMatcher.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();

var connectionString = builder.Configuration.GetConnectionString("NameMatcher")
    ?? throw new InvalidOperationException("Connection string 'NameMatcher' is missing.");

builder.Services.AddNameMatcherDatabase(connectionString);
builder.Services.AddHttpClient<NameMatcher.Core.IRegistryLookup, NameMatcher.Infrastructure.GleifRegistryLookup>(client =>
{
    client.BaseAddress = new Uri("https://api.gleif.org/");
    client.Timeout = TimeSpan.FromSeconds(20);
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();
app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();
