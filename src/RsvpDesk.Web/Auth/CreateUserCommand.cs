using Microsoft.EntityFrameworkCore;
using RsvpDesk.Web.Data;

namespace RsvpDesk.Web.Auth;

internal static class CreateUserCommand
{
    public static async Task<int> RunAsync(WebApplication app, string[] args)
    {
        if (args.Length < 2 || !Enum.TryParse<UserRole>(args[1], ignoreCase: true, out var role))
        {
            Console.Error.WriteLine("Usage: --create-user <email> <Organiser|Viewer>  (password from RSVP_NEW_USER_PASSWORD or stdin)");
            return 2;
        }

        var password = Environment.GetEnvironmentVariable("RSVP_NEW_USER_PASSWORD");
        if (string.IsNullOrEmpty(password))
        {
            Console.Error.Write("Password: ");
            password = Console.ReadLine() ?? "";
        }

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RsvpDbContext>();
        if (await db.Users.AnyAsync(u => u.Email == UserService.Normalise(args[0])))
        {
            Console.Error.WriteLine("A user with that email already exists.");
            return 1;
        }

        try
        {
            var user = await scope.ServiceProvider.GetRequiredService<UserService>().CreateAsync(args[0], password, role, CancellationToken.None);
            Console.WriteLine($"Created {user.Role} user {user.Id}.");
            return 0;
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }
}
