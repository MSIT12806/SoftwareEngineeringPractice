using MaskMap.Api.Domains;
using Microsoft.AspNetCore.Mvc;

namespace MaskMap.Api.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class PharmaciesController : ControllerBase
    {
        [HttpGet]
        public void GetByDistance([FromQuery] PharmacyQueryRequest req)
        {

        }

        [HttpGet("{pharmacyId}/inventories")]
        public IEnumerable<Inventory> GetInventories(string pharmacyId, [FromServices] InventoryQueryHandler inventoryQueryHandler)
        {
            var inventories = inventoryQueryHandler.GetInventoriesBy(pharmacyId);

            return inventories;
        }
    }
}
