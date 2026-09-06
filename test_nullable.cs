using System;
using System.Collections.Generic;
using System.Linq;

record BlobItemDto(string Name, long? Size, DateTime? LastModified, bool IsProcessed, string Status);

class Program
{
    static void Main()
    {
        var allBlobs = new List<BlobItemDto>
        {
            new BlobItemDto("file1", 50000, DateTime.Now, true, "Completed"),
            new BlobItemDto("file2", null, DateTime.Now, true, "Completed")
        };

        var filteredBlobs = allBlobs.AsEnumerable();
        filteredBlobs = filteredBlobs.Where(b => b.Status == "Completed");
        
        int? minSizeKb = 100;
        if (minSizeKb.HasValue)
        {
            var minBytes = minSizeKb.Value * 1024L;
            filteredBlobs = filteredBlobs.Where(b => b.Size >= minBytes);
        }

        Console.WriteLine(filteredBlobs.Count());
    }
}
