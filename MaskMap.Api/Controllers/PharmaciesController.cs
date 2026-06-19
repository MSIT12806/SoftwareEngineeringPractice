using MaskMap.Api.Domains;
using Microsoft.AspNetCore.Mvc;

namespace MaskMap.Api.Controllers
{
    [ApiController]
    [Route("api/pharmacies")]
    public class PharmaciesController : ControllerBase
    {
        [HttpGet]
        public void GetByDistance([FromQuery] PharmacyQueryRequest req)
        {

        }

        [HttpGet("{pharmacyId}/inventories")]
        public async Task<IActionResult> GetInventories(
            string pharmacyId,
            [FromServices] InventoryQueryHandler inventoryQueryHandler,
            CancellationToken cancellationToken)
        {
            var inventories = await inventoryQueryHandler.GetInventoriesByAsync(
                pharmacyId,
                cancellationToken);

            return Ok(new
            {
                PharmacyId = pharmacyId,
                Items = inventories
            });
        }
    }
}
