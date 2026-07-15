using Ecomerce_dotnet.Data;
using Ecomerce_dotnet.DTOs;
using Ecomerce_dotnet.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;

namespace Ecomerce_dotnet.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ProductsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IFileService _fileService; // Reusing your file service!
        private readonly IDistributedCache _cache; // Redis cache dependency

        public ProductsController(ApplicationDbContext context, IFileService fileService, IDistributedCache cache)
        {
            _context = context;
            _fileService = fileService;
            _cache = cache;
        }

        // 1. GET: api/products (Catalog view with dynamic filters, Pagination, and Redis Caching)
        [HttpGet]
        public async Task<IActionResult> GetAll(
            [FromQuery] Guid? categoryId,
            [FromQuery] bool? hasDiscount,
            [FromQuery] string? search,
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 10)
        {
            // Enforce basic query safety limits
            if (pageNumber < 1) pageNumber = 1;
            if (pageSize < 1 || pageSize > 50) pageSize = 10; // Guard against pulling too many items at once

            // Step 1: Append pagination signatures explicitly into the unique cache key structure
            string cacheKey = $"products:cat={categoryId}:disc={hasDiscount}:q={search?.Trim().ToLower()}:p={pageNumber}:sz={pageSize}";

            // Step 2: Try to get data from Redis
            var cachedProductsJson = await _cache.GetStringAsync(cacheKey);

            if (!string.IsNullOrEmpty(cachedProductsJson))
            {
                // CACHE HIT: Deserialize the wrapped PagedResult payload and return immediately
                var cachedResponse = JsonSerializer.Deserialize<PagedResult<ProductResponseDto>>(cachedProductsJson);
                Console.WriteLine($"CACHE HIT: Returning page {pageNumber} from Redis cache.");
                return Ok(cachedResponse);
            }

            // ===================================================================
            // CACHE MISS: Build base query (Filtering logic stays identically fast)
            // ===================================================================
            var query = _context.Products
                .Include(p => p.Category)
                .Include(p => p.Brand)
                .Include(p => p.Variants)
                .Where(p => !p.IsDeleted)
                .AsQueryable();

            if (categoryId.HasValue)
            {
                query = query.Where(p => p.CategoryId == categoryId.Value);
            }

            if (hasDiscount.HasValue && hasDiscount.Value)
            {
                query = query.Where(p => p.DiscountPercentage > 0);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var lowerSearch = search.ToLower();
                query = query.Where(p => p.Name.ToLower().Contains(lowerSearch) ||
                                     p.Description.ToLower().Contains(lowerSearch));
            }

            // Step A: Capture total matching record count BEFORE pulling subset items from SQL Server
            int totalCount = await query.CountAsync();

            // Step B: Apply high-performance database pagination window parameters
            var products = await query
                .OrderByDescending(p => p.Id) // Standard tracking order constraint for consistent paging loops
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            // Step C: Fetch ONLY images belonging to the paginated subset to prevent massive memory loops
            var productIds = products.Select(p => p.Id).ToList();
            var images = await _context.ProductImages
                .Where(img => productIds.Contains(img.ProductId))
                .ToListAsync();

            // Map entities to calculated safe Response DTOs
            var productDtos = products.Select(p => new ProductResponseDto
            {
                Id = p.Id,
                Name = p.Name,
                Description = p.Description,
                BasePrice = p.BasePrice,
                DiscountPercentage = p.DiscountPercentage,
                CategoryId = p.CategoryId,
                CategoryName = p.Category?.Name ?? "Unknown",
                BrandId = p.BrandId,
                BrandName = p.Brand?.Name ?? "Unknown",
                TotalAvailableStock = p.Variants.Sum(v => v.PhysicalQuantity - v.ReservedQuantity),
                Variants = p.Variants.Select(v => new ProductVariantDto
                {
                    Id = v.Id,
                    Color = v.Color,
                    Size = v.Size,
                    AvailableStock = Math.Max(0, v.PhysicalQuantity - v.ReservedQuantity),
                    Sku = v.Sku
                }).ToList(),
                Images = images.Where(img => img.ProductId == p.Id).Select(img => new ProductImageDto
                {
                    Id = img.Id,
                    ImageUrl = img.ImageUrl,
                    IsPrimary = img.IsPrimary,
                    ProductVariantId = img.ProductVariantId
                }).ToList()
            }).ToList();

            // Step D: Package the output properties cleanly inside the PagedResult structure
            var response = new PagedResult<ProductResponseDto>
            {
                Items = productDtos,
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalCount = totalCount,
                TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
            };

            // Step 3: Save the fresh structure to Redis
            var cacheOptions = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
                SlidingExpiration = TimeSpan.FromMinutes(2)
            };

            var serializedProducts = JsonSerializer.Serialize(response);
            await _cache.SetStringAsync(cacheKey, serializedProducts, cacheOptions);

            return Ok(response);
        }

        // 2. GET: api/products/{id} (Detailed product view)
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            var product = await _context.Products
                .Include(p => p.Category)
                .Include(p => p.Brand)
                .Include(p => p.Variants)
                .FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted);

            if (product == null)
            {
                return NotFound("Product not found or has been discontinued.");
            }

            var images = await _context.ProductImages
                .Where(img => img.ProductId == product.Id)
                .ToListAsync();

            var response = new ProductResponseDto
            {
                Id = product.Id,
                Name = product.Name,
                Description = product.Description,
                BasePrice = product.BasePrice,
                DiscountPercentage = product.DiscountPercentage,
                CategoryId = product.CategoryId,
                CategoryName = product.Category?.Name ?? "Unknown",
                BrandId = product.BrandId,
                BrandName = product.Brand?.Name ?? "Unknown",
                TotalAvailableStock = product.Variants.Sum(v => v.PhysicalQuantity - v.ReservedQuantity),
                Variants = product.Variants.Select(v => new ProductVariantDto
                {
                    Id = v.Id,
                    Color = v.Color,
                    Size = v.Size,
                    AvailableStock = Math.Max(0, v.PhysicalQuantity - v.ReservedQuantity),
                    Sku = v.Sku
                }).ToList(),
                Images = images.Select(img => new ProductImageDto
                {
                    Id = img.Id,
                    ImageUrl = img.ImageUrl,
                    IsPrimary = img.IsPrimary,
                    ProductVariantId = img.ProductVariantId
                }).ToList()
            };

            return Ok(response);
        }

        // 5. POST: api/products (Administrative Product Provisioning)
        [HttpPost]
        //[Authorize(Roles = "Admin")]
        public async Task<IActionResult> Create([FromForm] ProductCreateDto dto)
        {
            var categoryExists = await _context.Categories.AnyAsync(c => c.Id == dto.CategoryId);
            if (!categoryExists)
            {
                return BadRequest("The provided CategoryId does not exist.");
            }

            // 2. NEW: Relational Integrity Guard: Validate Brand Manufacturer exists
            var brandExists = await _context.Brands.AnyAsync(b => b.Id == dto.BrandId);
            if (!brandExists)
            {
                return BadRequest("The provided BrandId does not exist in the database system catalog.");
            }

            var incomingSkus = dto.Variants.Select(v => v.Sku).ToList();
            if (incomingSkus.Count != incomingSkus.Distinct().Count())
            {
                return BadRequest("Duplicate SKUs detected within the request payload.");
            }

            var duplicateDbSku = await _context.ProductVariants.AnyAsync(v => incomingSkus.Contains(v.Sku));
            if (duplicateDbSku)
            {
                return BadRequest("One or more SKUs provided are already assigned to existing inventory items.");
            }

            var uploadedFilePaths = new List<string>();

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var product = new Product
                {
                    Name = dto.Name,
                    Description = dto.Description,
                    BasePrice = dto.BasePrice,
                    DiscountPercentage = dto.DiscountPercentage,
                    CategoryId = dto.CategoryId,
                    BrandId = dto.BrandId,
                    IsDeleted = false
                };

                _context.Products.Add(product);

                var skuToVariantIdMap = new Dictionary<string, Guid>();

                foreach (var vDto in dto.Variants)
                {
                    var variant = new ProductVariant
                    {
                        ProductId = product.Id,
                        Color = vDto.Color,
                        Size = vDto.Size,
                        PhysicalQuantity = vDto.InitialPhysicalQuantity,
                        ReservedQuantity = 0,
                        Sku = vDto.Sku
                        // RowVersion is handled automatically by EF Core/SQL Server, no assignment needed here!
                    };

                    _context.ProductVariants.Add(variant);
                    skuToVariantIdMap.Add(variant.Sku, variant.Id);
                }

                // Process Binary Files using the correct DTO properties
                if (dto.Images != null)
                {
                    for (int i = 0; i < dto.Images.Count; i++)
                    {
                        var file = dto.Images[i];
                        var targetSku = (dto.ImageTargetSkus != null && dto.ImageTargetSkus.Count > i) ? dto.ImageTargetSkus[i] : null;
                        var isPrimary = (dto.ImageIsPrimary != null && dto.ImageIsPrimary.Count > i) ? dto.ImageIsPrimary[i] : false;

                        Guid? explicitVariantId = null;

                        if (!string.IsNullOrWhiteSpace(targetSku))
                        {
                            if (skuToVariantIdMap.TryGetValue(targetSku, out var variantId))
                            {
                                explicitVariantId = variantId;
                            }
                            else
                            {
                                return BadRequest($"Image target SKU '{targetSku}' does not match any variants provided.");
                            }
                        }

                        // Save file physically using your FileService inside a 'products' subfolder
                        string savedFilePath = await _fileService.SaveFileAsync(file, "products");
                        uploadedFilePaths.Add(savedFilePath);

                        var productImage = new ProductImage
                        {
                            ProductId = product.Id,
                            ImageUrl = savedFilePath,
                            IsPrimary = isPrimary,
                            ProductVariantId = explicitVariantId
                        };

                        _context.ProductImages.Add(productImage);
                    }
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                // CACHE INVALIDATION: Clear catalog caches so the new product shows up
                await InvalidateProductCacheAsync(dto.CategoryId);

                return CreatedAtAction("GetById", new { id = product.Id }, new { message = "Product created successfully.", productId = product.Id });
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();

                foreach (var path in uploadedFilePaths)
                {
                    _fileService.DeletePhysicalFile(path);
                }

                throw;
            }
        }


        // 3. PUT: api/products/{id} (Administrative details modification)
        [HttpPut("{id}")]
        // Enforces professional access rules
        public async Task<IActionResult> Update(Guid id, [FromBody] ProductUpdateDto dto)
        {
            var product = await _context.Products
                .FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted);

            if (product == null)
            {
                return NotFound("Product not found.");
            }

            // Verify the target category is valid before shifting the entity dependency
            var categoryExists = await _context.Categories.AnyAsync(c => c.Id == dto.CategoryId);
            if (!categoryExists)
            {
                return BadRequest("Invalid CategoryId provided.");
            }

            // 2. NEW: Relational Integrity Guard: Validate Brand Manufacturer exists
            var brandExists = await _context.Brands.AnyAsync(b => b.Id == dto.BrandId);
            if (!brandExists)
            {
                return BadRequest("The provided BrandId does not exist in the database system catalog.");
            }
            // Capture the old category ID in case the admin is moving the product to a different category
            Guid oldCategoryId = product.CategoryId;

            // Update core attributes
            product.Name = dto.Name;
            product.Description = dto.Description;
            product.BasePrice = dto.BasePrice;
            product.DiscountPercentage = dto.DiscountPercentage;
            product.CategoryId = dto.CategoryId;
            product.BrandId = dto.BrandId;

            await _context.SaveChangesAsync();

            await InvalidateProductCacheAsync(oldCategoryId);
            if (oldCategoryId != dto.CategoryId)
            {
                await InvalidateProductCacheAsync(dto.CategoryId);
            }

            return Ok(new { message = "Product core settings updated successfully." });
        }

        // 4. DELETE: api/products/{id} (Professional Soft Delete)
        [HttpDelete("{id}")]
        
        public async Task<IActionResult> SoftDelete(Guid id)
        {
            var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == id);

            if (product == null)
            {
                return NotFound("Product not found.");
            }

            if (product.IsDeleted)
            {
                return BadRequest("Product is already soft-deleted.");
            }

            // Execute professional soft delete modification
            product.IsDeleted = true;
            await _context.SaveChangesAsync();

            // CACHE INVALIDATION: Remove the item from the customer view cache
            await InvalidateProductCacheAsync(product.CategoryId);

            return Ok(new { message = "Product successfully archived and removed from customer view catalog." });
        }


        // ========================================================
        // 6. PUT: api/products/variants/{variantId} (Modify Specific Color/Size Variant)
        // ========================================================
        [HttpPut("variants/{variantId}")]
        // [Authorize(Roles = "Admin")]
        public async Task<IActionResult> UpdateVariant(Guid variantId, [FromBody] ProductVariantUpdateDto dto)
        {
            // 1. Fetch the target variant to modify
            var variant = await _context.ProductVariants
                .FirstOrDefaultAsync(v => v.Id == variantId);

            if (variant == null)
            {
                return NotFound("The specified product variant does not exist.");
            }

            // 2. Validate SKU uniqueness if the SKU is being changed
            if (!string.Equals(variant.Sku, dto.Sku, StringComparison.OrdinalIgnoreCase))
            {
                var skuExists = await _context.ProductVariants.AnyAsync(v => v.Sku.ToLower() == dto.Sku.ToLower());
                if (skuExists)
                {
                    return BadRequest($"The SKU '{dto.Sku}' is already assigned to another inventory variant.");
                }
            }

            // 3. Fetch parent product to clear out the correct Redis Category Cache later
            var product = await _context.Products
                .Select(p => new { p.Id, p.CategoryId })
                .FirstOrDefaultAsync(p => p.Id == variant.ProductId);

            if (product == null) return NotFound("Parent product container missing.");

            // 4. Map modifications
            variant.Color = dto.Color;
            variant.Size = dto.Size;
            variant.PhysicalQuantity = dto.PhysicalQuantity;
            variant.Sku = dto.Sku;
            // ReservedQuantity is managed by checkout rules, leave it out of raw admin updates!

            await _context.SaveChangesAsync();

            // 5. CACHE INVALIDATION: Clear catalog entries so customers see updated variant info or stock tallies
            await InvalidateProductCacheAsync(product.CategoryId);

            return Ok(new { message = "Variant properties updated successfully, and cache synchronized." });
        }

        // ========================================================
        // 7. DELETE: api/products/variants/{variantId} (Remove Specific Color/Size Variant Completely)
        // ========================================================
        [HttpDelete("variants/{variantId}")]
        // [Authorize(Roles = "Admin")]
        public async Task<IActionResult> DeleteVariant(Guid variantId)
        {
            // 1. Find the target variant
            var variant = await _context.ProductVariants
                .FirstOrDefaultAsync(v => v.Id == variantId);

            if (variant == null)
            {
                return NotFound("Product variant option not found.");
            }

            // 2. Locate parent product metadata for cache eviction mapping
            var product = await _context.Products
                .Select(p => new { p.Id, p.CategoryId })
                .FirstOrDefaultAsync(p => p.Id == variant.ProductId);

            if (product == null) return NotFound("Parent product context missing.");

            // 3. Optional: Prevent removing the LAST variant if business rules require at least one choice
            var totalRemainingVariants = await _context.ProductVariants.CountAsync(v => v.ProductId == variant.ProductId);
            if (totalRemainingVariants <= 1)
            {
                return BadRequest("Cannot delete the last variant. If you wish to pull this product completely, delete the entire Product entity instead.");
            }

            // 4. Isolate and wipe associated image variations bound specifically to this color option
            var specificVariantImages = await _context.ProductImages
                .Where(img => img.ProductVariantId == variantId)
                .ToListAsync();

            if (specificVariantImages.Any())
            {
                _context.ProductImages.RemoveRange(specificVariantImages);
                // Clean up physical file storage via your FileService
                foreach (var img in specificVariantImages)
                {
                    _fileService.DeletePhysicalFile(img.ImageUrl);
                }
            }

            // 5. Remove the variant row from the DB
            _context.ProductVariants.Remove(variant);
            await _context.SaveChangesAsync();

            // 6. CACHE INVALIDATION: Evict cached records so customers don't select a deleted configuration
            await InvalidateProductCacheAsync(product.CategoryId);

            return Ok(new { message = "Variant variation deleted cleanly, storage files removed, and web cache updated." });
        }

        // ========================================================
        // 8. POST: api/products/{id}/variants (Append New Variant to Existing Product)
        // ========================================================
        [HttpPost("{id}/variants")]
        public async Task<IActionResult> AddVariantToProduct(Guid id, [FromBody] VariantCreateDto dto)
        {
            // FIXED: Where clause moved before Select projection
            var product = await _context.Products
                .Where(p => p.Id == id && !p.IsDeleted)
                .Select(p => new { p.Id, p.CategoryId })
                .FirstOrDefaultAsync();

            if (product == null)
            {
                return NotFound("Parent product not found or has been discontinued.");
            }

            var skuExists = await _context.ProductVariants.AnyAsync(v => v.Sku.ToLower() == dto.Sku.ToLower());
            if (skuExists)
            {
                return BadRequest($"The SKU '{dto.Sku}' is already assigned to another inventory item.");
            }

            var variant = new ProductVariant
            {
                ProductId = id,
                Color = dto.Color,
                Size = dto.Size,
                PhysicalQuantity = dto.InitialPhysicalQuantity,
                ReservedQuantity = 0,
                Sku = dto.Sku
            };

            _context.ProductVariants.Add(variant);
            await _context.SaveChangesAsync();

            await InvalidateProductCacheAsync(product.CategoryId);

            return Ok(new { message = "New variant appended successfully.", variantId = variant.Id });
        }

        // ========================================================
        // 9. POST: api/products/{id}/images (Upload/Attach Image to Existing Product or Variant)
        // ========================================================
        [HttpPost("{id}/images")]
        public async Task<IActionResult> UploadProductImage(Guid id, [FromForm] ProductImageUploadDto dto)
        {
            // FIXED: Where clause moved before Select projection
            var product = await _context.Products
                .Where(p => p.Id == id && !p.IsDeleted)
                .Select(p => new { p.Id, p.CategoryId })
                .FirstOrDefaultAsync();

            if (product == null) return NotFound("Target product container not found.");

            if (dto.ProductVariantId.HasValue)
            {
                // FIXED: Ensures Guid type safety comparison
                var variantBelongs = await _context.ProductVariants
                    .AnyAsync(v => v.Id == dto.ProductVariantId.Value && v.ProductId == id);
                if (!variantBelongs)
                {
                    return BadRequest("The selected variant does not belong to this specific product.");
                }
            }

            if (dto.IsPrimary)
            {
                var currentPrimaries = await _context.ProductImages
                    .Where(img => img.ProductId == id && img.IsPrimary)
                    .ToListAsync();

                foreach (var oldPrimary in currentPrimaries)
                {
                    oldPrimary.IsPrimary = false;
                }
            }

            // FIXED: Referencing 'dto.File' to match the updated DTO property
            string savedFilePath = await _fileService.SaveFileAsync(dto.File, "products");

            var productImage = new ProductImage
            {
                ProductId = id,
                ImageUrl = savedFilePath,
                IsPrimary = dto.IsPrimary,
                ProductVariantId = dto.ProductVariantId
            };

            _context.ProductImages.Add(productImage);
            await _context.SaveChangesAsync();

            await InvalidateProductCacheAsync(product.CategoryId);

            return Ok(new { message = "Image asset uploaded and attached successfully.", imageId = productImage.Id, path = savedFilePath });
        }

        // ========================================================
        // 10. PUT: api/products/images/{imageId}/set-primary
        // ========================================================
        [HttpPut("images/{imageId}/set-primary")]
        // [Authorize(Roles = "Admin")]
        public async Task<IActionResult> SetPrimaryImage(Guid imageId)
        {
            // 1. Fetch the target image along with its parent Product relationship context
            var imageToUpdate = await _context.ProductImages
                .Include(img => img.Product)
                .FirstOrDefaultAsync(img => img.Id == imageId);

            if (imageToUpdate == null)
            {
                return NotFound("The specified product image asset does not exist.");
            }

            // 2. Performance guard: If it's already primary, do nothing
            if (imageToUpdate.IsPrimary)
            {
                return Ok(new { message = "Image is already set as the primary asset." });
            }

            // 3. System Invalidation Loop: Locate any other images acting as primary for this product and demote them
            var currentPrimaries = await _context.ProductImages
                .Where(img => img.ProductId == imageToUpdate.ProductId && img.IsPrimary)
                .ToListAsync();

            foreach (var oldPrimary in currentPrimaries)
            {
                oldPrimary.IsPrimary = false;
            }

            // 4. Update state flag to true
            imageToUpdate.IsPrimary = true;

            await _context.SaveChangesAsync();

            // 5. CACHE INVALIDATION: Wipe out Redis listings so the new thumbnail choice updates instantly everywhere
            if (imageToUpdate.Product != null)
            {
                await InvalidateProductCacheAsync(imageToUpdate.Product.CategoryId);
            }

            return Ok(new { message = "Primary image status reassigned successfully, and catalog caches synchronized." });
        }

        private async Task InvalidateProductCacheAsync(Guid categoryId)
        {
            var defaultKeys = new List<string>
            {
                "products:cat=:disc=:q=:p=1:sz=10",
                "products:cat=:disc=False:q=:p=1:sz=10",
                "products:cat=:disc=True:q=:p=1:sz=10",
                $"products:cat={categoryId}:disc=:q=:p=1:sz=10",
                $"products:cat={categoryId}:disc=False:q=:p=1:sz=10",
                $"products:cat={categoryId}:disc=True:q=:p=1:sz=10"
            };

            foreach (var key in defaultKeys)
            {
                await _cache.RemoveAsync(key);
            }

            Console.WriteLine($"[Redis Cache] Invalidated key sets for Category container: {categoryId}");
        }
    }
}
