using System.Text;
using System.Text.Json.Nodes;

using PaymentOps.Data.Demo.Dataset;

namespace PaymentOps.Data.ContractTests;

/// <summary>A small valid dataset as JSON, for tests that break one thing and check the loader notices.</summary>
internal static class DatasetJson
{
    public static JsonObject Valid() => JsonNode.Parse("""
        {
          "asOf": "2026-11-03T18:00:00Z",
          "processors": [
            { "id": "p1", "name": "Processor One",
              "configuration": { "cutOff": "16:00:00", "timeZoneId": "America/New_York", "expectedDelivery": "02:00:00", "lateAfter": "02:00:00", "holdingSettlementDays": 0 } }
          ],
          "closureDays": [ { "date": "2026-11-11", "reason": "Closure day" } ],
          "merchants": [
            { "id": "M-1", "name": "Merchant One", "processorIds": ["p1"],
              "riskHold": { "id": "RH-1", "placedAt": "2026-11-01T10:00:00Z", "ruleId": "RF-37", "reviewBy": "2026-11-20" } }
          ],
          "payments": [
            { "id": "PAY-1", "merchantId": "M-1", "processorId": "p1", "amount": 25.50, "currency": "USD",
              "capturedAt": "2026-11-02T15:00:00Z", "processorCapturedAt": "2026-11-02T15:00:02Z", "batchDate": "2026-11-02" }
          ],
          "files": [
            { "id": "SF-1", "processorId": "p1", "batchDate": "2026-11-02", "receivedAt": "2026-11-03T07:00:00Z", "status": "Accepted",
              "lines": [ { "merchantId": "M-1", "paymentIds": ["PAY-1"], "amount": 25.50, "currency": "USD" } ] }
          ],
          "payouts": [
            { "id": "PO-1", "merchantId": "M-1", "processorId": "p1", "batchDate": "2026-11-02", "sourceFileIds": ["SF-1"],
              "scheduledFor": "2026-11-03", "amount": 25.50, "currency": "USD", "status": "Paid", "paidAt": "2026-11-03T12:00:00Z" }
          ],
          "incidents": [
            { "id": "INC-1", "processorId": "p1", "component": "FileDelivery", "summary": "Delayed files",
              "startedAt": "2026-11-02T19:00:00Z", "resolvedAt": "2026-11-02T22:00:00Z", "processorReference": "P1-77" }
          ],
          "errorEvents": [
            { "id": "EV-1", "occurredAt": "2026-11-02T19:30:00Z", "processorId": "p1", "merchantId": "M-1",
              "category": "FileTransferTimeout", "code": "SFTP-TIMEOUT", "fileId": "SF-1" }
          ]
        }
        """)!.AsObject();

    public static JsonObject First(this JsonObject dataset, string section) => dataset[section]![0]!.AsObject();

    public static DemoDataset Load(JsonObject dataset) =>
        DemoDatasetLoader.Load(new MemoryStream(Encoding.UTF8.GetBytes(dataset.ToJsonString())));

    public static IReadOnlyList<string> Errors(JsonObject dataset) =>
        Assert.Throws<InvalidDemoDatasetException>(() => Load(dataset)).Errors;
}
