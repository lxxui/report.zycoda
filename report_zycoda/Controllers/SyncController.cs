using Microsoft.AspNetCore.Mvc;
using System;

[ApiController]
[Route("api/[controller]")]
public class SyncController : ControllerBase
{
    private readonly LatestSyncStore _syncStore;

    public SyncController(LatestSyncStore syncStore)
    {
        _syncStore = syncStore;
    }

    [HttpGet("last-time")]
    public IActionResult GetLastSyncTime()
    {
        if (_syncStore.LastSyncTime.HasValue)
        {
            return Ok(new
            {
                success = true,
                lastSync = _syncStore.LastSyncTime.Value.ToString("dd/MM/yyyy HH:mm:ss")
            });
        }
        return Ok(new { success = false, message = "ระบบกำลังดึงข้อมูลรอบแรก..." });
    }
}