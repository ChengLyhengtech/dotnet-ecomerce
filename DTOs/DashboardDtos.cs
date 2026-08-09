using System;
using System.Collections.Generic;

namespace Ecomerce_dotnet.DTOs
{
    public class DashboardSummaryDto
    {
        public FinancialsDto Financials { get; set; } = new();
        public int TotalUserCount { get; set; }
        public List<InventoryAlertDto> InventoryAlerts { get; set; } = new();
        public List<GeospatialLogisticsDto> GeospatialLogistics { get; set; } = new();
        public List<TopPerformerDto> TopProducts { get; set; } = new();
        public List<TopPerformerDto> TopBrands { get; set; } = new();
        public List<TopPerformerDto> TopCategories { get; set; } = new();
    }

    public class FinancialsDto
    {
        public decimal GrossRevenue { get; set; }
        public decimal TotalCOGS { get; set; }          // Cost of Goods Sold
        public decimal NetProfit { get; set; }          // GrossRevenue - TotalCOGS
        public decimal ProfitMarginPercentage { get; set; } // (NetProfit / GrossRevenue) * 100
        public int TotalOrders { get; set; }
        public decimal AverageOrderValue { get; set; }
    }

    public class TopPerformerDto
    {
        public Guid? Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int TotalQuantitySold { get; set; }
        public decimal TotalRevenue { get; set; }
    }

    public class InventoryAlertDto
    {
        public Guid VariantId { get; set; }
        public Guid ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public string Sku { get; set; } = string.Empty;
        public string Color { get; set; } = string.Empty;
        public string Size { get; set; } = string.Empty;
        public int PhysicalQuantity { get; set; }
        public int ReservedQuantity { get; set; }
        public int AvailableStock { get; set; }
    }

    public class GeospatialLogisticsDto
    {
        public Guid OrderId { get; set; }
        public double DeliveryLatitude { get; set; }
        public double DeliveryLongitude { get; set; }
        public decimal TotalAmount { get; set; }
    }
}
