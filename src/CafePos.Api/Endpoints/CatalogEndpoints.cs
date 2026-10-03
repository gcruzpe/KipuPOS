using CafePos.Api.Data;
using CafePos.Api.Domain;
using CafePos.Shared;
using Microsoft.EntityFrameworkCore;

namespace CafePos.Api.Endpoints;

public static class CatalogEndpoints
{
    public static void MapCatalog(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api").RequireAuthorization();
        var admin = app.MapGroup("/api").RequireAuthorization("Admin");

        // Lectura: cualquier usuario autenticado
        g.MapGet("/categories", async (AppDbContext db, bool? onlyActive) =>
            await db.Categories.AsNoTracking()
                .Where(c => onlyActive != true || c.Active)
                .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
                .Select(c => new CategoryDto(c.Id, c.Name, c.SortOrder, c.Active)).ToListAsync());

        g.MapGet("/products", async (AppDbContext db, Guid? categoryId, bool? onlyActive) =>
            await db.Products.AsNoTracking()
                .Where(p => (categoryId == null || p.CategoryId == categoryId) && (onlyActive != true || p.Active))
                .OrderBy(p => p.Name)
                .Select(p => new ProductDto(p.Id, p.CategoryId, p.Name, p.Price, p.Active)).ToListAsync());

        // Escritura: solo Admin
        admin.MapPost("/categories", async (CategoryUpsert r, AppDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(r.Name)) return Results.BadRequest("Nombre requerido.");
            var c = new Category { Name = r.Name.Trim(), SortOrder = r.SortOrder, Active = r.Active };
            db.Categories.Add(c);
            await db.SaveChangesAsync();
            return Results.Created($"/api/categories/{c.Id}", new CategoryDto(c.Id, c.Name, c.SortOrder, c.Active));
        });

        admin.MapPut("/categories/{id:guid}", async (Guid id, CategoryUpsert r, AppDbContext db) =>
        {
            var c = await db.Categories.FindAsync(id);
            if (c is null) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(r.Name)) return Results.BadRequest("Nombre requerido.");
            c.Name = r.Name.Trim(); c.SortOrder = r.SortOrder; c.Active = r.Active;
            await db.SaveChangesAsync();
            return Results.Ok(new CategoryDto(c.Id, c.Name, c.SortOrder, c.Active));
        });

        admin.MapPost("/products", async (ProductUpsert r, AppDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(r.Name) || r.Price < 0) return Results.BadRequest("Datos inválidos.");
            if (!await db.Categories.AnyAsync(c => c.Id == r.CategoryId)) return Results.BadRequest("Categoría inexistente.");
            var p = new Product { CategoryId = r.CategoryId, Name = r.Name.Trim(), Price = decimal.Round(r.Price, 2), Active = r.Active };
            db.Products.Add(p);
            await db.SaveChangesAsync();
            return Results.Created($"/api/products/{p.Id}", new ProductDto(p.Id, p.CategoryId, p.Name, p.Price, p.Active));
        });

        admin.MapPut("/products/{id:guid}", async (Guid id, ProductUpsert r, AppDbContext db) =>
        {
            var p = await db.Products.FindAsync(id);
            if (p is null) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(r.Name) || r.Price < 0) return Results.BadRequest("Datos inválidos.");
            if (!await db.Categories.AnyAsync(c => c.Id == r.CategoryId)) return Results.BadRequest("Categoría inexistente.");
            p.CategoryId = r.CategoryId; p.Name = r.Name.Trim(); p.Price = decimal.Round(r.Price, 2); p.Active = r.Active;
            await db.SaveChangesAsync();
            return Results.Ok(new ProductDto(p.Id, p.CategoryId, p.Name, p.Price, p.Active));
        });

        // No se borra: se desactiva, para conservar el historial de ventas
        admin.MapDelete("/products/{id:guid}", async (Guid id, AppDbContext db) =>
        {
            var p = await db.Products.FindAsync(id);
            if (p is null) return Results.NotFound();
            p.Active = false;
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }
}
