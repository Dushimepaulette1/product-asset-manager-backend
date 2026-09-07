using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProductAssetManager.Api.DTOs;
using ProductAssetManager.Api.Services;

namespace ProductAssetManager.Api.Controllers;

[ApiController]
[Route("api/collections")]
public class CollectionsController : ControllerBase
{
    private readonly ICollectionService _collectionService;

    public CollectionsController(ICollectionService collectionService)
    {
        _collectionService = collectionService;
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Create(CreateCollectionRequest request)
    {
        var collection = await _collectionService.CreateAsync(request);
        return CreatedAtAction(nameof(GetById), new { id = collection.Id }, collection);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var collection = await _collectionService.GetByIdAsync(id);

        if (collection is null)
        {
            return NotFound(new { message = $"Collection '{id}' was not found." });
        }

        return Ok(collection);
    }

    [HttpPost("{id:guid}/products")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> AddProduct(Guid id, AddProductToCollectionRequest request)
    {
        var result = await _collectionService.AddProductAsync(id, request.ProductId);

        if (result.CollectionNotFound)
        {
            return NotFound(new { message = $"Collection '{id}' was not found." });
        }

        if (result.ProductNotFound)
        {
            return NotFound(new { message = $"Product '{request.ProductId}' was not found." });
        }

        if (result.AlreadyMember)
        {
            return Ok(result.Collection);
        }

        return StatusCode(201, result.Collection);
    }

    [HttpDelete("{id:guid}/products/{productId:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> RemoveProduct(Guid id, Guid productId)
    {
        var result = await _collectionService.RemoveProductAsync(id, productId);

        if (result.CollectionNotFound)
        {
            return NotFound(new { message = $"Collection '{id}' was not found." });
        }

        if (result.ProductNotFound)
        {
            return NotFound(new { message = $"Product '{productId}' was not found." });
        }

        return NoContent();
    }
}
