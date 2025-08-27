using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

public class MangoAuthDbContext : IdentityDbContext
{
    public MangoAuthDbContext(DbContextOptions<MangoAuthDbContext> options) :
        base(options)
    {
    }
}