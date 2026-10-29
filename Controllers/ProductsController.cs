using Ecomerce_dotnet.Data;
using Ecomerce_dotnet.DTOs;
using Ecomerce_dotnet.Filters;
using Ecomerce_dotnet.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;

namespace Ecomerce_dotnet.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [EnableRateLimiting("ApiPolicy")]
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

        // 1. GET: api/products (Catalog view with dynamic filters, Pagination, Price Range, Brand, and Redis Caching)
        [HttpGet]
        public async Task<IActionResult> GetAll(
            [FromQuery] Guid? categoryId,
            [FromQuery] Guid? brandId,
            [FromQuery] bool? hasDiscount,
            [FromQuery] string? search,
            [FromQuery] decimal? minPrice,
            [FromQuery] decimal? maxPrice,
            [FromQuery] TargetGender? targetGender,
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 10)
        {
            // Enforce basic query safety limits
            if (pageNumber < 1) pageNumber = 1;
            if (pageSize < 1 || pageSize > 50) pageSize = 10; // Guard against pulling too many items at once

            // Step 1: Append pagination, price, brand, and gender signatures explicitly into the unique cache key structure
            string cacheKey = $"products:cat={categoryId}:brand={brandId}:disc={hasDiscount}:q={search?.Trim().ToLower()}:minp={minPrice}:maxp={maxPrice}:gen={targetGender}:p={pageNumber}:sz={pageSize}";

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

            if (brandId.HasValue)
            {
                query = query.Where(p => p.BrandId == brandId.Value);
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

            if (minPrice.HasValue)
            {
                query = query.Where(p => (p.BasePrice * (1 - (p.DiscountPercentage / 100m))) >= minPrice.Value);
            }

            if (maxPrice.HasValue)
            {
                query = query.Where(p => (p.BasePrice * (1 - (p.DiscountPercentage / 100m))) <= maxPrice.Value);
            }

            // Bitwise Flag Filtering for Multi-Demographic Targeting
            if (targetGender.HasValue && targetGender.Value != TargetGender.None)
            {
                query = query.Where(p => (p.TargetGender & targetGender.Value) != 0);
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
                TargetGender = p.TargetGender,
                TotalAvailableStock = p.Variants.Sum(v => v.PhysicalQuantity - v.ReservedQuantity),
                Variants = p.Variants.Select(v => new ProductVariantDto
                {
                    Id = v.Id,
                    Color = v.Color,
                    Size = v.Size,
                    AvailableStock = Math.Max(0, v.PhysicalQuantity - v.ReservedQuantity),
                    CostPrice = v.CostPrice,
                    Sku = v.Sku
                }).ToList(),
                Images = images.Where(img => img.ProductId == p.Id)
                    .OrderByDescending(img => img.IsPrimary)
                    .Take(1)
                    .Select(img => new ProductImageDto
                    {
                        Id = img.Id,
                        ImageUrl = img.ImageUrl,
                        IsPrimary = img.IsPrimary,
                        Color = img.Color
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

        // 2. GET: api/products/under-price?price=20 (Products under specific price threshold, defaults to $20)
        [HttpGet("under-price")]
        public async Task<IActionResult> GetUnderPrice(
            [FromQuery] decimal price = 20m,
            [FromQuery] TargetGender? targetGender = null,
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 10)
        {
            return await GetAll(
                categoryId: null,
                brandId: null,
                hasDiscount: null,
                search: null,
                minPrice: null,
                maxPrice: price,
                targetGender: targetGender,
                pageNumber: pageNumber,
                pageSize: pageSize);
        }

        // 3. GET: api/products/top-selling?count=10 (Top N most sold products)
        [HttpGet("top-selling")]
        public async Task<IActionResult> GetTopSelling([FromQuery] int count = 10)
        {
            if (count < 1 || count > 50) count = 10;

            string cacheKey = $"products:top-selling:count={count}";

            var cachedProductsJson = await _cache.GetStringAsync(cacheKey);

            if (!string.IsNullOrEmpty(cachedProductsJson))
            {
                var cachedResponse = JsonSerializer.Deserialize<List<ProductResponseDto>>(cachedProductsJson);
                return Ok(cachedResponse);
            }

            // Step 1: Group valid OrderItems by ProductId to rank by total quantity sold
            var topSalesGrouped = await _context.OrderItems
                .Where(oi => oi.Order != null 
                    && oi.Order.Status != "Pending" 
                    && oi.Order.Status != "Cancelled" 
                    && oi.ProductVariant != null 
                    && oi.ProductVariant.Product != null 
                    && !oi.ProductVariant.Product.IsDeleted)
                .GroupBy(oi => oi.ProductVariant!.ProductId)
                .Select(g => new
                {
                    ProductId = g.Key,
                    TotalQuantitySold = g.Sum(x => x.Quantity)
                })
                .OrderByDescending(x => x.TotalQuantitySold)
                .Take(count)
                .ToListAsync();

            var topProductIds = topSalesGrouped.Select(x => x.ProductId).ToList();

            // Step 2: If fewer orders exist than count requested, backfill with active products
            if (topProductIds.Count < count)
            {
                var remainingCount = count - topProductIds.Count;
                var fallbackIds = await _context.Products
                    .Where(p => !p.IsDeleted && !topProductIds.Contains(p.Id))
                    .OrderByDescending(p => p.Id)
                    .Take(remainingCount)
                    .Select(p => p.Id)
                    .ToListAsync();

                topProductIds.AddRange(fallbackIds);
            }

            // Step 3: Fetch full entities with Category, Brand, and Variants
            var products = await _context.Products
                .Include(p => p.Category)
                .Include(p => p.Brand)
                .Include(p => p.Variants)
                .Where(p => topProductIds.Contains(p.Id) && !p.IsDeleted)
                .ToListAsync();

            // Maintain top-selling rank ordering
            var orderedProducts = topProductIds
                .Select(id => products.FirstOrDefault(p => p.Id == id))
                .Where(p => p != null)
                .Cast<Product>()
                .ToList();

            var productIds = orderedProducts.Select(p => p.Id).ToList();
            var images = await _context.ProductImages
                .Where(img => productIds.Contains(img.ProductId))
                .ToListAsync();

            var resultDtos = orderedProducts.Select(p => new ProductResponseDto
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
                TargetGender = p.TargetGender,
                TotalAvailableStock = p.Variants.Sum(v => v.PhysicalQuantity - v.ReservedQuantity),
                Variants = p.Variants.Select(v => new ProductVariantDto
                {
                    Id = v.Id,
                    Color = v.Color,
                    Size = v.Size,
                    AvailableStock = Math.Max(0, v.PhysicalQuantity - v.ReservedQuantity),
                    CostPrice = v.CostPrice,
                    Sku = v.Sku
                }).ToList(),
                Images = images.Where(img => img.ProductId == p.Id)
                    .OrderByDescending(img => img.IsPrimary)
                    .Take(1)
                    .Select(img => new ProductImageDto
                    {
                        Id = img.Id,
                        ImageUrl = img.ImageUrl,
                        IsPrimary = img.IsPrimary,
                        Color = img.Color
                    }).ToList()
            }).ToList();

            var cacheOptions = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
                SlidingExpiration = TimeSpan.FromMinutes(2)
            };

            await _cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(resultDtos), cacheOptions);

            return Ok(resultDtos);
        }

        // 4. GET: api/products/{id} (Detailed product view)
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
                TargetGender = product.TargetGender,
                TotalAvailableStock = product.Variants.Sum(v => v.PhysicalQuantity - v.ReservedQuantity),
                Variants = product.Variants.Select(v => new ProductVariantDto
                {
                    Id = v.Id,
                    Color = v.Color,
                    Size = v.Size,
                    AvailableStock = Math.Max(0, v.PhysicalQuantity - v.ReservedQuantity),
                    CostPrice = v.CostPrice,
                    Sku = v.Sku
                }).ToList(),
                Images = images.Select(img => new ProductImageDto
                {
                    Id = img.Id,
                    ImageUrl = img.ImageUrl,
                    IsPrimary = img.IsPrimary,
                    Color = img.Color
                }).ToList()
            };

            return Ok(response);
        }

        // 5. POST: api/products (Administrative Product Provisioning)
        [HttpPost]
        [HasPermission("products:create")]
        public async Task<IActionResult> Create([FromForm] ProductCreateDto dto)
        {
            var categoryExists = await _context.Categories.AnyAsync(c => c.Id == dto.CategoryId);
            if (!categoryExists)
            {
                return BadRequest("The provided CategoryId does not exist.");
            }

            var brandExists = await _context.Brands.AnyAsync(b => b.Id == dto.BrandId);
            if (!brandExists)
            {
                return BadRequest("The provided BrandId does not exist in the database system catalog.");
            }

            // 1. Check for explicit SKU duplicates in the request payload (ignoring null/empty/whitespace)
            List<string> rawPayloadSkus = new();
            if (dto.ColorGroups != null && dto.ColorGroups.Any())
            {
                rawPayloadSkus = dto.ColorGroups
                    .SelectMany(cg => cg.Sizes)
                    .Select(s => s.Sku?.Trim())
                    .Where(sku => !string.IsNullOrWhiteSpace(sku))
                    .Cast<string>()
                    .ToList();
            }
            else if (dto.Variants != null && dto.Variants.Any())
            {
                rawPayloadSkus = dto.Variants
                    .Select(v => v.Sku?.Trim())
                    .Where(sku => !string.IsNullOrWhiteSpace(sku))
                    .Cast<string>()
                    .ToList();
            }

            var duplicatePayloadSkus = rawPayloadSkus
                .GroupBy(sku => sku, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            if (duplicatePayloadSkus.Any())
            {
                return BadRequest(new { 
                    message = $"Duplicate SKUs detected within request payload: {string.Join(", ", duplicatePayloadSkus)}" 
                });
            }

            // 2. Extract incoming variant definitions from ColorGroups (or fallback Variants if ColorGroups is empty)
            var variantsToCreate = new List<(string Color, string Size, int Quantity, string Sku, decimal UnitCost)>();

            if (dto.ColorGroups != null && dto.ColorGroups.Any())
            {
                foreach (var group in dto.ColorGroups)
                {
                    if (string.IsNullOrWhiteSpace(group.Color))
                        return BadRequest("Each ColorGroup must specify a valid Color name.");

                    foreach (var sizeItem in group.Sizes)
                    {
                        if (string.IsNullOrWhiteSpace(sizeItem.Size))
                            return BadRequest($"Size is required for color group '{group.Color}'.");

                        string finalSku = !string.IsNullOrWhiteSpace(sizeItem.Sku)
                            ? sizeItem.Sku.Trim()
                            : $"{dto.Name.Replace(" ", "-")}-{group.Color.Trim()}-{sizeItem.Size.Trim()}".ToUpper();

                        variantsToCreate.Add((group.Color.Trim(), sizeItem.Size.Trim(), sizeItem.PhysicalQuantity, finalSku, sizeItem.UnitCost));
                    }
                }
            }
            else if (dto.Variants != null && dto.Variants.Any())
            {
                foreach (var vDto in dto.Variants)
                {
                    string finalSku = !string.IsNullOrWhiteSpace(vDto.Sku)
                        ? vDto.Sku.Trim()
                        : $"{dto.Name.Replace(" ", "-")}-{vDto.Color.Trim()}-{vDto.Size.Trim()}".ToUpper();

                    variantsToCreate.Add((vDto.Color.Trim(), vDto.Size.Trim(), vDto.InitialPhysicalQuantity, finalSku, vDto.UnitCost));
                }
            }
            else
            {
                return BadRequest("Product must contain at least one color group or variant option.");
            }

            // 3. Check for existing SKUs in the Database
            var incomingSkus = variantsToCreate.Select(v => v.Sku).ToList();
            var existingDbSkus = await _context.ProductVariants
                .Where(v => incomingSkus.Contains(v.Sku))
                .Select(v => v.Sku)
                .ToListAsync();

            if (existingDbSkus.Any())
            {
                return BadRequest(new { 
                    message = $"One or more SKUs provided are already assigned to existing inventory items: {string.Join(", ", existingDbSkus)}" 
                });
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
                    TargetGender = dto.TargetGender,
                    IsDeleted = false
                };

                _context.Products.Add(product);

                foreach (var item in variantsToCreate)
                {
                    var variant = new ProductVariant
                    {
                        ProductId = product.Id,
                        Color = item.Color,
                        Size = item.Size,
                        PhysicalQuantity = item.Quantity,
                        ReservedQuantity = 0,
                        CostPrice = item.UnitCost,
                        Sku = item.Sku
                    };

                    _context.ProductVariants.Add(variant);

                    if (variant.PhysicalQuantity > 0)
                    {
                        var movement = new StockMovement
                        {
                            ProductVariantId = variant.Id,
                            QuantityChanged = variant.PhysicalQuantity,
                            UnitCost = item.UnitCost,
                            MovementType = StockMovementType.Restock,
                            ChangedBy = GetUserEmail(),
                            CreatedAt = DateTime.UtcNow
                        };
                        _context.StockMovements.Add(movement);
                    }
                }

                // Process Binary Files mapped by Color
                if (dto.Images != null)
                {
                    for (int i = 0; i < dto.Images.Count; i++)
                    {
                        var file = dto.Images[i];
                        var imageColor = (dto.ImageColors != null && dto.ImageColors.Count > i) ? dto.ImageColors[i]?.Trim() : null;
                        var isPrimary = (dto.ImageIsPrimary != null && dto.ImageIsPrimary.Count > i) ? dto.ImageIsPrimary[i] : false;

                        string savedFilePath = await _fileService.SaveFileAsync(file, "products");
                        uploadedFilePaths.Add(savedFilePath);

                        var productImage = new ProductImage
                        {
                            ProductId = product.Id,
                            ImageUrl = savedFilePath,
                            IsPrimary = isPrimary,
                            Color = imageColor
                        };

                        _context.ProductImages.Add(productImage);
                    }
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

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
        [HasPermission("products:update")]
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
            product.TargetGender = dto.TargetGender;

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
        [HasPermission("products:delete")]
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
        [HasPermission("products:update")]
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

            // 4. Map modifications and log inventory audit adjustments
            int oldPhysicalQuantity = variant.PhysicalQuantity;
            int quantityDifference = dto.PhysicalQuantity - oldPhysicalQuantity;

            variant.Color = dto.Color;
            variant.Size = dto.Size;
            variant.PhysicalQuantity = dto.PhysicalQuantity;
            variant.Sku = dto.Sku;

            if (dto.CostPrice.HasValue)
            {
                variant.CostPrice = dto.CostPrice.Value;
            }
            else if (dto.UnitCost.HasValue)
            {
                variant.CostPrice = dto.UnitCost.Value;
            }

            // ReservedQuantity is managed by checkout rules, leave it out of raw admin updates!

            if (quantityDifference != 0)
            {
                var movement = new StockMovement
                {
                    ProductVariantId = variant.Id,
                    QuantityChanged = quantityDifference,
                    UnitCost = variant.CostPrice,
                    MovementType = StockMovementType.AuditCorrection,
                    ChangedBy = GetUserEmail(),
                    CreatedAt = DateTime.UtcNow
                };
                _context.StockMovements.Add(movement);
            }

            await _context.SaveChangesAsync();

            // 5. CACHE INVALIDATION: Clear catalog entries so customers see updated variant info or stock tallies
            await InvalidateProductCacheAsync(product.CategoryId);

            return Ok(new { message = "Variant properties updated successfully, and cache synchronized." });
        }

        // ========================================================
        // 7. DELETE: api/products/variants/{variantId} (Remove Specific Color/Size Variant Completely)
        // ========================================================
        [HttpDelete("variants/{variantId}")]
        [HasPermission("products:delete")]
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

            // 4. Isolate and wipe associated image variations bound specifically to this color option if no other variants share this color
            bool otherVariantsExistWithColor = await _context.ProductVariants
                .AnyAsync(v => v.ProductId == variant.ProductId && v.Id != variantId && v.Color.ToLower() == variant.Color.ToLower());

            if (!otherVariantsExistWithColor && !string.IsNullOrWhiteSpace(variant.Color))
            {
                var colorImages = await _context.ProductImages
                    .Where(img => img.ProductId == variant.ProductId && img.Color != null && img.Color.ToLower() == variant.Color.ToLower())
                    .ToListAsync();

                if (colorImages.Any())
                {
                    _context.ProductImages.RemoveRange(colorImages);
                    foreach (var img in colorImages)
                    {
                        _fileService.DeletePhysicalFile(img.ImageUrl);
                    }
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
        [HasPermission("products:update")]
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
                CostPrice = dto.UnitCost,
                Sku = dto.Sku
            };

            _context.ProductVariants.Add(variant);

            if (variant.PhysicalQuantity > 0)
            {
                var movement = new StockMovement
                {
                    ProductVariantId = variant.Id,
                    QuantityChanged = variant.PhysicalQuantity,
                    UnitCost = dto.UnitCost,
                    MovementType = StockMovementType.Restock,
                    ChangedBy = GetUserEmail(),
                    CreatedAt = DateTime.UtcNow
                };
                _context.StockMovements.Add(movement);
            }

            await _context.SaveChangesAsync();

            await InvalidateProductCacheAsync(product.CategoryId);

            return Ok(new { message = "New variant appended successfully.", variantId = variant.Id });
        }

        // ========================================================
        // 9. POST: api/products/{id}/images (Upload/Attach Image to Existing Product or Variant)
        // ========================================================
        [HttpPost("{id}/images")]
        [HasPermission("products:update")]
        public async Task<IActionResult> UploadProductImage(Guid id, [FromForm] ProductImageUploadDto dto)
        {
            var product = await _context.Products
                .Where(p => p.Id == id && !p.IsDeleted)
                .Select(p => new { p.Id, p.CategoryId })
                .FirstOrDefaultAsync();

            if (product == null) return NotFound("Target product container not found.");

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

            string savedFilePath = await _fileService.SaveFileAsync(dto.File, "products");

            var productImage = new ProductImage
            {
                ProductId = id,
                ImageUrl = savedFilePath,
                IsPrimary = dto.IsPrimary,
                Color = dto.Color?.Trim()
            };

            _context.ProductImages.Add(productImage);
            await _context.SaveChangesAsync();

            await InvalidateProductCacheAsync(product.CategoryId);

            return Ok(new { message = "Image asset uploaded and attached successfully.", imageId = productImage.Id, path = savedFilePath, color = productImage.Color });
        }

        // ========================================================
        // 10. PUT: api/products/images/{imageId}/set-primary
        // ========================================================
        [HttpPut("images/{imageId}/set-primary")]
        [HasPermission("products:update")]
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

        // ========================================================
        // 11. DELETE: api/products/images/{imageId} (Delete Single Product Image)
        // ========================================================
        [HttpDelete("images/{imageId}")]
        [HasPermission("products:delete")]
        public async Task<IActionResult> DeleteProductImage(Guid imageId)
        {
            // 1. Find & Validate target product image
            var image = await _context.ProductImages
                .Include(img => img.Product)
                .FirstOrDefaultAsync(img => img.Id == imageId);

            if (image == null)
            {
                return NotFound("Target product image asset not found.");
            }

            // 2. Physical File Cleanup
            if (!string.IsNullOrEmpty(image.ImageUrl))
            {
                try
                {
                    _fileService.DeletePhysicalFile(image.ImageUrl);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[WARNING] Failed to delete physical image file '{image.ImageUrl}' for imageId '{imageId}': {ex.Message}");
                }
            }

            // 3. Primary Image Reassignment (if deleting current primary image)
            if (image.IsPrimary)
            {
                var nextPrimaryImage = await _context.ProductImages
                    .FirstOrDefaultAsync(img => img.ProductId == image.ProductId && img.Id != imageId);

                if (nextPrimaryImage != null)
                {
                    nextPrimaryImage.IsPrimary = true;
                }
            }

            // 4. Database Purge
            _context.ProductImages.Remove(image);
            await _context.SaveChangesAsync();

            // 5. Cache Invalidation
            if (image.Product != null)
            {
                await InvalidateProductCacheAsync(image.Product.CategoryId);
            }

            // 6. Response
            return Ok(new { message = "Product image deleted successfully.", imageId });
        }

        private async Task InvalidateProductCacheAsync(Guid categoryId)
        {
            try
            {
                var defaultKeys = new List<string>();

                string[] cats = new string[] { "", categoryId.ToString() };
                bool?[] discs = new bool?[] { null, false, true };
                int[] pageSizes = new int[] { 10, 20, 50 };
                int[] pageNumbers = new int[] { 1, 2, 3, 4, 5 };

                foreach (var cat in cats)
                {
                    foreach (var disc in discs)
                    {
                        foreach (var sz in pageSizes)
                        {
                            foreach (var p in pageNumbers)
                            {
                                defaultKeys.Add($"products:cat={cat}:brand=:disc={disc}:q=:minp=:maxp=:gen=:p={p}:sz={sz}");
                                defaultKeys.Add($"products:cat={cat}:disc={disc}:q=:p={p}:sz={sz}");
                            }
                        }
                    }
                }

                defaultKeys.Add("products:top-selling:count=5");
                defaultKeys.Add("products:top-selling:count=10");
                defaultKeys.Add("products:top-selling:count=20");

                foreach (var key in defaultKeys)
                {
                    await _cache.RemoveAsync(key);
                }

                Console.WriteLine($"[Redis Cache] Invalidated key sets for Category container: {categoryId}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Redis Cache WARNING] Failed to invalidate cache for category {categoryId}: {ex.Message}");
            }
        }

        // ========================================================
        // 12. POST: api/products/reviews (Customer Review & Rating Submission with Order Status Guard)
        // ========================================================
        [HttpPost("reviews")]
        [Authorize]
        public async Task<IActionResult> SubmitReview([FromBody] ProductReviewCreateDto dto)
        {
            var userId = GetUserId();

            // 1. Verify Order exists and belongs to current user
            var order = await _context.Orders
                .Include(o => o.OrderItems)
                    .ThenInclude(oi => oi.ProductVariant)
                .FirstOrDefaultAsync(o => o.Id == dto.OrderId);

            if (order == null)
            {
                return NotFound("The specified order does not exist.");
            }

            if (order.UserId != userId)
            {
                return Forbid();
            }

            // 2. Strict Guard: Order status must be Received or Completed
            var status = order.Status?.Trim();
            bool isReceivedOrCompleted = string.Equals(status, "Received", StringComparison.OrdinalIgnoreCase) 
                || string.Equals(status, "Completed", StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, OrderStatus.Received.ToString(), StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, OrderStatus.Completed.ToString(), StringComparison.OrdinalIgnoreCase);

            if (!isReceivedOrCompleted)
            {
                return BadRequest(new 
                { 
                    message = $"Reviews can only be submitted for orders that are 'Received' or 'Completed'. Current order status is '{order.Status}'." 
                });
            }

            // 3. Verify Product was purchased in this order
            var orderItem = order.OrderItems
                .FirstOrDefault(oi => oi.ProductVariant != null && oi.ProductVariant.ProductId == dto.ProductId);

            if (orderItem == null)
            {
                return BadRequest(new { message = "The specified product was not purchased as part of this order." });
            }

            // 4. Prevent duplicate reviews for the same product in the same order
            var existingReview = await _context.ProductReviews
                .AnyAsync(r => r.OrderId == dto.OrderId && r.ProductId == dto.ProductId && r.UserId == userId);

            if (existingReview)
            {
                return BadRequest(new { message = "You have already submitted a rating/review for this product in this order." });
            }

            // 5. Create Review Entity
            var review = new ProductReview
            {
                OrderId = dto.OrderId,
                ProductId = dto.ProductId,
                UserId = userId,
                Rating = dto.Rating,
                Comment = dto.Comment?.Trim() ?? string.Empty,
                CreatedAt = DateTime.UtcNow
            };

            _context.ProductReviews.Add(review);

            // 6. Transition order status from Received to Completed upon successful review submission
            if (string.Equals(order.Status, "Received", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(order.Status, OrderStatus.Received.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                order.Status = OrderStatus.Completed.ToString();
            }

            await _context.SaveChangesAsync();

            var user = await _context.Users.FindAsync(userId);
            var product = await _context.Products.FindAsync(dto.ProductId);

            var response = new ProductReviewResponseDto
            {
                Id = review.Id,
                ProductId = review.ProductId,
                ProductName = product?.Name ?? "Product",
                OrderId = review.OrderId,
                UserId = review.UserId,
                UserName = user?.FullName ?? user?.UserName ?? "Customer",
                Rating = review.Rating,
                Comment = review.Comment,
                CreatedAt = review.CreatedAt
            };

            return CreatedAtAction(nameof(GetProductReviews), new { id = dto.ProductId }, response);
        }

        // ========================================================
        // 13. GET: api/products/{id}/reviews (Public Product Reviews & Rating Summary)
        // ========================================================
        [HttpGet("{id}/reviews")]
        public async Task<IActionResult> GetProductReviews(Guid id)
        {
            var productExists = await _context.Products.AnyAsync(p => p.Id == id && !p.IsDeleted);
            if (!productExists)
            {
                return NotFound("Target product not found.");
            }

            var reviews = await _context.ProductReviews
                .Where(r => r.ProductId == id)
                .Include(r => r.User)
                .Include(r => r.Product)
                .OrderByDescending(r => r.CreatedAt)
                .Select(r => new ProductReviewResponseDto
                {
                    Id = r.Id,
                    ProductId = r.ProductId,
                    ProductName = r.Product != null ? r.Product.Name : string.Empty,
                    OrderId = r.OrderId,
                    UserId = r.UserId,
                    UserName = r.User != null ? (r.User.FullName ?? r.User.UserName ?? "Customer") : "Customer",
                    Rating = r.Rating,
                    Comment = r.Comment,
                    CreatedAt = r.CreatedAt
                })
                .ToListAsync();

            double avgRating = reviews.Count > 0 ? Math.Round(reviews.Average(r => r.Rating), 1) : 0.0;

            var result = new ProductReviewsSummaryDto
            {
                ProductId = id,
                AverageRating = avgRating,
                TotalReviews = reviews.Count,
                Reviews = reviews
            };

            return Ok(result);
        }

        private Guid GetUserId()
        {
            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                ?? User.FindFirst("sub")?.Value
                ?? User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            {
                throw new UnauthorizedAccessException("User context is invalid or token missing.");
            }
            return userId;
        }

        private string GetUserEmail()
        {
            return User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value 
                ?? User.Identity?.Name 
                ?? "System";
        }
    }
}
