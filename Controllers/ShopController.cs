using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ecomerce_dotnet.Data;
using Ecomerce_dotnet.DTOs;
using Ecomerce_dotnet.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;

namespace Ecomerce_dotnet.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ShopController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IFileService _fileService;
        private readonly IDistributedCache _cache;
        private const string ProfileCacheKey = "shop:profile";
        private const string ContactsCacheKey = "shop:contacts:active";

        private static readonly string[] AllowedImageExtensions = { ".jpg", ".jpeg", ".png", ".gif", ".svg", ".webp" };

        public ShopController(ApplicationDbContext context, IFileService fileService, IDistributedCache cache)
        {
            _context = context;
            _fileService = fileService;
            _cache = cache;
        }

        // ==========================================
        // SECTION 1: SHOP PROFILE ENDPOINTS
        // ==========================================

        /// <summary>
        /// GET /api/shop/profile
        /// Get complete shop profile with location and active dynamic contacts.
        /// </summary>
        [HttpGet("profile")]
        public async Task<IActionResult> GetShopProfile()
        {
            var profile = await GetOrCreateProfileAsync();

            var contacts = await _context.ShopContacts
                .Where(c => c.IsActive)
                .OrderBy(c => c.DisplayOrder)
                .ThenBy(c => c.Title)
                .Select(c => MapToContactResponseDto(c))
                .ToListAsync();

            var response = MapToProfileResponseDto(profile, contacts);
            return Ok(response);
        }

        /// <summary>
        /// PUT /api/shop/profile
        /// Admin/Manager: Update shop profile details (name, description, email, phone, logo, banner).
        /// </summary>
        [HttpPut("profile")]
        [HttpPost("profile")]
        [Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> UpdateShopProfile([FromForm] ShopProfileUpdateDto dto)
        {
            var profile = await GetOrCreateProfileAsync();

            profile.ShopName = dto.ShopName;
            profile.Description = dto.Description;
            profile.Email = dto.Email;
            profile.Phone = dto.Phone;
            profile.Address = dto.Address ?? profile.Address;
            profile.GoogleMapUrl = dto.GoogleMapUrl ?? profile.GoogleMapUrl;
            profile.Latitude = dto.Latitude ?? profile.Latitude;
            profile.Longitude = dto.Longitude ?? profile.Longitude;
            profile.OpeningHours = dto.OpeningHours ?? profile.OpeningHours;
            if (dto.OrderCancellationWindowHours.HasValue)
            {
                profile.OrderCancellationWindowHours = dto.OrderCancellationWindowHours.Value;
            }

            // Handle Logo File Upload
            if (dto.LogoImage != null && dto.LogoImage.Length > 0)
            {
                ValidateImageFile(dto.LogoImage);
                if (!string.IsNullOrEmpty(profile.LogoUrl))
                {
                    _fileService.DeletePhysicalFile(profile.LogoUrl);
                }
                profile.LogoUrl = await _fileService.SaveFileAsync(dto.LogoImage, "shop");
            }

            profile.UpdatedAt = DateTime.UtcNow;
            _context.Entry(profile).State = EntityState.Modified;
            await _context.SaveChangesAsync();

            await InvalidateCacheAsync();

            var contacts = await _context.ShopContacts
                .Where(c => c.IsActive)
                .OrderBy(c => c.DisplayOrder)
                .Select(c => MapToContactResponseDto(c))
                .ToListAsync();

            return Ok(MapToProfileResponseDto(profile, contacts));
        }

        // ==========================================
        // SECTION 2: SHOP LOCATION ENDPOINTS
        // ==========================================

        /// <summary>
        /// GET /api/shop/location
        /// Get shop location information.
        /// </summary>
        [HttpGet("location")]
        public async Task<IActionResult> GetShopLocation()
        {
            var profile = await GetOrCreateProfileAsync();
            var locationDto = MapToLocationDto(profile);
            return Ok(locationDto);
        }

        /// <summary>
        /// PUT /api/shop/location
        /// Admin/Manager: Update shop location information.
        /// </summary>
        [HttpPut("location")]
        [HttpPost("location")]
        [Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> UpdateShopLocation([FromBody] ShopLocationDto dto)
        {
            var profile = await GetOrCreateProfileAsync();

            profile.Address = dto.Address;
            profile.GoogleMapUrl = dto.GoogleMapUrl;
            profile.Latitude = dto.Latitude;
            profile.Longitude = dto.Longitude;
            profile.OpeningHours = dto.OpeningHours;
            profile.UpdatedAt = DateTime.UtcNow;

            _context.Entry(profile).State = EntityState.Modified;
            await _context.SaveChangesAsync();

            await InvalidateCacheAsync();

            return Ok(MapToLocationDto(profile));
        }

        // ==========================================
        // SECTION 3: DYNAMIC CONTACTS ENDPOINTS
        // ==========================================

        /// <summary>
        /// GET /api/shop/contacts
        /// Public: Get all active dynamic contacts sorted by DisplayOrder.
        /// </summary>
        [HttpGet("contacts")]
        public async Task<IActionResult> GetActiveContacts()
        {
            var contacts = await _context.ShopContacts
                .Where(c => c.IsActive)
                .OrderBy(c => c.DisplayOrder)
                .ThenBy(c => c.Title)
                .Select(c => MapToContactResponseDto(c))
                .ToListAsync();

            return Ok(contacts);
        }

        /// <summary>
        /// GET /api/shop/contacts/admin
        /// Admin/Manager: Get all dynamic contacts (active and inactive).
        /// </summary>
        [HttpGet("contacts/admin")]
        [Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> GetAllContactsAdmin()
        {
            var contacts = await _context.ShopContacts
                .OrderBy(c => c.DisplayOrder)
                .ThenBy(c => c.Title)
                .Select(c => MapToContactResponseDto(c))
                .ToListAsync();

            return Ok(contacts);
        }

        /// <summary>
        /// GET /api/shop/contacts/{id}
        /// Get a single contact by ID.
        /// </summary>
        [HttpGet("contacts/{id}")]
        public async Task<IActionResult> GetContactById(Guid id)
        {
            var contact = await _context.ShopContacts.FindAsync(id);
            if (contact == null) return NotFound("Target shop contact entity not found.");

            return Ok(MapToContactResponseDto(contact));
        }

        /// <summary>
        /// POST /api/shop/contacts
        /// Admin/Manager: Add a new dynamic contact with uploaded icon/image asset.
        /// </summary>
        [HttpPost("contacts")]
        [Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> CreateContact([FromForm] ShopContactCreateDto dto)
        {
            if (dto.IconImage == null || dto.IconImage.Length == 0)
                return BadRequest("A valid icon or image asset file is required.");

            ValidateImageFile(dto.IconImage);

            string iconPath = await _fileService.SaveFileAsync(dto.IconImage, "contacts");

            var contact = new ShopContact
            {
                Title = dto.Title,
                ProfileUrl = dto.ProfileUrl,
                IconUrl = iconPath,
                ContactType = string.IsNullOrWhiteSpace(dto.ContactType) ? "Social" : dto.ContactType,
                DisplayOrder = dto.DisplayOrder,
                IsActive = dto.IsActive,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.ShopContacts.Add(contact);
            await _context.SaveChangesAsync();

            await InvalidateCacheAsync();

            return CreatedAtAction(nameof(GetContactById), new { id = contact.Id }, MapToContactResponseDto(contact));
        }

        /// <summary>
        /// PUT /api/shop/contacts/{id}
        /// Admin/Manager: Update contact details and optionally replace icon file.
        /// </summary>
        [HttpPut("contacts/{id}")]
        [Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> UpdateContact(Guid id, [FromForm] ShopContactUpdateDto dto)
        {
            var contact = await _context.ShopContacts.FindAsync(id);
            if (contact == null) return NotFound("Target shop contact entity not found.");

            contact.Title = dto.Title;
            contact.ProfileUrl = dto.ProfileUrl;
            contact.ContactType = string.IsNullOrWhiteSpace(dto.ContactType) ? contact.ContactType : dto.ContactType;
            contact.DisplayOrder = dto.DisplayOrder;
            contact.IsActive = dto.IsActive;
            contact.UpdatedAt = DateTime.UtcNow;

            // Handle optional replacement icon image
            if (dto.IconImage != null && dto.IconImage.Length > 0)
            {
                ValidateImageFile(dto.IconImage);
                if (!string.IsNullOrEmpty(contact.IconUrl))
                {
                    _fileService.DeletePhysicalFile(contact.IconUrl);
                }
                contact.IconUrl = await _fileService.SaveFileAsync(dto.IconImage, "contacts");
            }

            _context.Entry(contact).State = EntityState.Modified;
            await _context.SaveChangesAsync();

            await InvalidateCacheAsync();

            return Ok(MapToContactResponseDto(contact));
        }

        /// <summary>
        /// PATCH /api/shop/contacts/{id}/toggle-active
        /// Admin/Manager: Toggle active status of a contact.
        /// </summary>
        [HttpPatch("contacts/{id}/toggle-active")]
        [Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> ToggleContactActive(Guid id)
        {
            var contact = await _context.ShopContacts.FindAsync(id);
            if (contact == null) return NotFound("Target shop contact entity not found.");

            contact.IsActive = !contact.IsActive;
            contact.UpdatedAt = DateTime.UtcNow;

            _context.Entry(contact).State = EntityState.Modified;
            await _context.SaveChangesAsync();

            await InvalidateCacheAsync();

            return Ok(MapToContactResponseDto(contact));
        }

        /// <summary>
        /// DELETE /api/shop/contacts/{id}
        /// Admin/Manager: Delete a dynamic contact and remove its icon file asset.
        /// </summary>
        [HttpDelete("contacts/{id}")]
        [Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> DeleteContact(Guid id)
        {
            var contact = await _context.ShopContacts.FindAsync(id);
            if (contact == null) return NotFound("Target shop contact entity not found.");

            if (!string.IsNullOrEmpty(contact.IconUrl))
            {
                _fileService.DeletePhysicalFile(contact.IconUrl);
            }

            _context.ShopContacts.Remove(contact);
            await _context.SaveChangesAsync();

            await InvalidateCacheAsync();

            return NoContent();
        }

        // ==========================================
        // PRIVATE HELPER METHODS
        // ==========================================

        private async Task<ShopProfile> GetOrCreateProfileAsync()
        {
            var profile = await _context.ShopProfiles.FirstOrDefaultAsync();
            if (profile == null)
            {
                profile = new ShopProfile
                {
                    ShopName = "My E-Commerce Store",
                    Description = "Your premier destination for high quality products.",
                    Email = "support@ecommerce.com",
                    Phone = "+855 12 345 678",
                    Address = "St 2004, Phnom Penh, Cambodia",
                    GoogleMapUrl = "https://maps.google.com/?q=11.5564,104.9282",
                    Latitude = 11.5564,
                    Longitude = 104.9282,
                    OpeningHours = "Mon - Sun: 8:00 AM - 8:00 PM",
                    OrderCancellationWindowHours = 6,
                    UpdatedAt = DateTime.UtcNow
                };
                _context.ShopProfiles.Add(profile);
                await _context.SaveChangesAsync();
            }
            return profile;
        }

        private void ValidateImageFile(IFormFile file)
        {
            var extension = System.IO.Path.GetExtension(file.FileName).ToLower();
            if (!AllowedImageExtensions.Contains(extension))
            {
                throw new ArgumentException($"Invalid file format '{extension}'. Supported formats: {string.Join(", ", AllowedImageExtensions)}.");
            }
        }

        private async Task InvalidateCacheAsync()
        {
            try
            {
                await _cache.RemoveAsync(ProfileCacheKey);
                await _cache.RemoveAsync(ContactsCacheKey);
            }
            catch
            {
                // Silently swallow cache errors if Redis is down
            }
        }

        private static ShopProfileResponseDto MapToProfileResponseDto(ShopProfile profile, List<ShopContactResponseDto> contacts)
        {
            return new ShopProfileResponseDto
            {
                Id = profile.Id,
                ShopName = profile.ShopName,
                Description = profile.Description,
                LogoUrl = profile.LogoUrl,
                Email = profile.Email,
                Phone = profile.Phone,
                Address = profile.Address,
                GoogleMapUrl = profile.GoogleMapUrl,
                Latitude = profile.Latitude,
                Longitude = profile.Longitude,
                OpeningHours = profile.OpeningHours,
                OrderCancellationWindowHours = profile.OrderCancellationWindowHours,
                UpdatedAt = profile.UpdatedAt,
                Contacts = contacts
            };
        }

        private static ShopLocationDto MapToLocationDto(ShopProfile profile)
        {
            return new ShopLocationDto
            {
                Address = profile.Address,
                GoogleMapUrl = profile.GoogleMapUrl,
                Latitude = profile.Latitude,
                Longitude = profile.Longitude,
                OpeningHours = profile.OpeningHours
            };
        }

        private static ShopContactResponseDto MapToContactResponseDto(ShopContact contact)
        {
            return new ShopContactResponseDto
            {
                Id = contact.Id,
                Title = contact.Title,
                ProfileUrl = contact.ProfileUrl,
                IconUrl = contact.IconUrl,
                ContactType = contact.ContactType,
                DisplayOrder = contact.DisplayOrder,
                IsActive = contact.IsActive,
                CreatedAt = contact.CreatedAt,
                UpdatedAt = contact.UpdatedAt
            };
        }
    }
}
