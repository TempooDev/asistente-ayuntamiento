using System;
using System.Collections.Generic;
using System.Linq;

record BlobItemDto(string Name, long? Size, DateTime? LastModified, bool IsProcessed, string Status);

class Program
{
    static void Main()
    {
        var allBlobs = new List<BlobItemDto>();
        for(int i = 0; i < 100; i++) 
            allBlobs.Add(new BlobItemDto($"file{i}", i % 2 == 0 ? 50000 : null, DateTime.Now, true, "Completed"));
        
        var filteredBlobs = allBlobs.AsEnumerable();
        
        string status = "Procesados";
        if (status == "Procesados")
            filteredBlobs = filteredBlobs.Where(b => b.Status == "Completed");
            
        int? minSizeKb = 100;
        if (minSizeKb.HasValue)
        {
            var minBytes = minSizeKb.Value * 1024L;
            filteredBlobs = filteredBlobs.Where(b => b.Size >= minBytes);
        }

        try {
            Console.WriteLine($"Count: {filteredBlobs.Count()}");
        } catch (Exception ex) {
            Console.WriteLine(ex);
        }
    }
}
