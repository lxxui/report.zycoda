using System;

public class LatestSyncStore
{
    // ตัวแปรเก็บเวลาล่าสุด (Default เป็น null ถ้ายังไม่ได้รันครั้งแรก)
    public DateTime? LastSyncTime { get; set; }
}