using System.Runtime.InteropServices;

namespace Sitas.Edge.EdgePlcDriver.DataTypes;

/// <summary>
/// Example UDT classes showing how to define structures for PLC communication.
/// Copy and modify these examples for your own UDT definitions.
/// </summary>
/// <remarks>
/// IMPORTANT RULES FOR UDT CLASSES:
/// 1. MUST use [StructLayout(LayoutKind.Sequential)] attribute
/// 2. MUST use public FIELDS (not properties)
/// 3. Fields MUST be in the EXACT ORDER as the PLC UDT definition
/// 4. Initialize arrays and nested types in the field declaration
/// 5. Use correct .NET types that map to Logix types
/// </remarks>
public static class ExampleUdtTemplates
{
    // This class is intentionally empty - it serves as documentation
    // See the example classes below
}

/// <summary>
/// Example: Simple UDT with primitive types.
/// 
/// RSLogix/Studio 5000 UDT Definition:
/// Name: MACHINE_STATUS
/// Members:
///   running     BOOL    Decimal
///   faulted     BOOL    Decimal
///   speed       REAL    Float
///   count       DINT    Decimal
///   errorCode   INT     Decimal
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public class MachineStatusUdt
{
    /// <summary>
    /// Indicates if the machine is currently running.
    /// </summary>
    public Boolean running;
    
    /// <summary>
    /// Indicates if the machine is in a faulted state.
    /// </summary>
    public Boolean faulted;
    
    /// <summary>
    /// Current speed of the machine.
    /// </summary>
    public Single speed;
    
    /// <summary>
    /// Production count value.
    /// </summary>
    public Int32 count;
    
    /// <summary>
    /// Error code if the machine is faulted.
    /// </summary>
    public Int16 errorCode;
}

/// <summary>
/// Example: UDT with arrays.
/// 
/// RSLogix/Studio 5000 UDT Definition:
/// Name: HOURLY_DATA
/// Members:
///   active       BOOL        Decimal
///   hourlyCount  INT[24]     Decimal
///   totalRate    REAL        Float
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public class HourlyDataUdt
{
    /// <summary>
    /// Indicates if hourly data collection is active.
    /// </summary>
    public Boolean active;
    
    /// <summary>
    /// Array of 24 hourly count values. Array MUST be initialized.
    /// </summary>
    public Int16[] hourlyCount = new Int16[24];  // Array MUST be initialized
    
    /// <summary>
    /// Total production rate.
    /// </summary>
    public Single totalRate;
}

/// <summary>
/// Example: UDT with STRING type.
/// 
/// RSLogix/Studio 5000 UDT Definition:
/// Name: PRODUCT_INFO
/// Members:
///   productCode  STRING    
///   batchNumber  DINT      Decimal
///   weight       REAL      Float
///   description  STRING    
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public class ProductInfoUdt
{
    /// <summary>
    /// Product code string. STRING must be initialized.
    /// </summary>
    public LogixString productCode = new();      // STRING must be initialized
    
    /// <summary>
    /// Batch number for this product.
    /// </summary>
    public Int32 batchNumber;
    
    /// <summary>
    /// Weight of the product.
    /// </summary>
    public Single weight;
    
    /// <summary>
    /// Product description string. STRING must be initialized.
    /// </summary>
    public LogixString description = new();      // STRING must be initialized
}

/// <summary>
/// Example: Nested UDTs (UDT containing another UDT).
/// 
/// RSLogix/Studio 5000 UDT Definition:
/// Name: WORK_CELL
/// Members:
///   cellId        DINT           Decimal
///   machine1      MACHINE_STATUS
///   machine2      MACHINE_STATUS
///   totalOutput   DINT           Decimal
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public class WorkCellUdt
{
    /// <summary>
    /// Unique identifier for this work cell.
    /// </summary>
    public Int32 cellId;
    
    /// <summary>
    /// Status of machine 1. Nested UDT must be initialized.
    /// </summary>
    public MachineStatusUdt machine1 = new();    // Nested UDT must be initialized
    
    /// <summary>
    /// Status of machine 2. Nested UDT must be initialized.
    /// </summary>
    public MachineStatusUdt machine2 = new();    // Nested UDT must be initialized
    
    /// <summary>
    /// Total output from this work cell.
    /// </summary>
    public Int32 totalOutput;
}

/// <summary>
/// Example: UDT with array of UDTs.
/// 
/// RSLogix/Studio 5000 UDT Definition:
/// Name: PRODUCTION_LINE
/// Members:
///   lineId        STRING
///   cells         WORK_CELL[4]
///   efficiency    REAL          Float
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public class ProductionLineUdt
{
    /// <summary>
    /// Production line identifier string.
    /// </summary>
    public LogixString lineId = new();

    /// <summary>
    /// Array of work cells. Array of UDTs - MUST initialize each element.
    /// </summary>
    public WorkCellUdt[] cells = 
    [
        new(), new(), new(), new()
    ];

    /// <summary>
    /// Overall efficiency of the production line.
    /// </summary>
    public Single efficiency;
}

/// <summary>
/// Example: UDT matching a sample active data structure.
/// Shows complex nested structures with multiple levels.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public class SampleDataUdt
{
    /// <summary>
    /// Unique identifier for this sample.
    /// </summary>
    public LogixString sampleId = new();
    
    /// <summary>
    /// Timestamp when the sample was taken.
    /// </summary>
    public LogixString sampledOn = new();
    
    /// <summary>
    /// Identifier of who or what took the sample.
    /// </summary>
    public LogixString sampledBy = new();
}

/// <summary>
/// Example: Position data structure.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public class PositionDataUdt
{
    /// <summary>
    /// X coordinate location in millimeters.
    /// </summary>
    public Single xLocMm;
    
    /// <summary>
    /// Y coordinate location in millimeters.
    /// </summary>
    public Single yLocMm;
    
    /// <summary>
    /// Z coordinate location in millimeters.
    /// </summary>
    public Single zLocMm;
    
    /// <summary>
    /// Theta angle location in degrees.
    /// </summary>
    public Single thetaLocDeg;
}

/// <summary>
/// Example: Analysis result structure.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public class AnalysisResultUdt
{
    /// <summary>
    /// Status code of the measurement.
    /// </summary>
    public Int32 measurementStatus;
    
    /// <summary>
    /// Result value of the analysis.
    /// </summary>
    public Int32 result;
}

/// <summary>
/// Example: Complete sample with position and analysis.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public class CompleteSampleUdt
{
    /// <summary>
    /// Site identifier where the sample was taken.
    /// </summary>
    public Int32 site;
    
    /// <summary>
    /// Lot number for this sample.
    /// </summary>
    public LogixString lotNumber = new();
    
    /// <summary>
    /// Position data where the sample was taken.
    /// </summary>
    public PositionDataUdt position = new();
    
    /// <summary>
    /// Analysis results for this sample.
    /// </summary>
    public AnalysisResultUdt analysis = new();
}
