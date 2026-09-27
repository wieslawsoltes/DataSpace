namespace DataSpace.Core;

/// <summary>Optional identity lookup for lazy record sources, without materializing every record.</summary>
public interface IRecordIndex
{
    int IndexOfRecord(string identity);
}
