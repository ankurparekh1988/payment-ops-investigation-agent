// Evaluation harness. Every command signs in with the Azure CLI session; key authentication is disabled on
// every service. investigation-eval runs the labelled investigation cases against the deployed model and index
// and gates them on the frozen thresholds; retrieval-eval runs the held-out retrieval cases, query only;
// thresholds derives the thresholds file from a baseline; kill-test compares the agent with the fixed workflow.
// retrieval-compare runs the retrieval calibration in both modes and recommends one. None of them writes to
// Search or edits the AI release. Exit codes: 0 passed or report-only, 1 failed, 2 bad command or input,
// 3 the evaluation environment was unavailable.
//
// Usage: PaymentOps.Evaluation investigation-eval --foundry <url> --search <url> --scope <token scope> --ai <dir> --sources <dir> --dataset <file> [--dataset <file>] --runs <n> --system agent|fixed-workflow --pricing <file> [--thresholds <file>] [--no-judge] --out <file> --summary <file>
//        PaymentOps.Evaluation retrieval-eval --endpoint <url> --ai <dir> --sources <dir> --dataset <file> [--thresholds <file>] --out <file> --summary <file>
//        PaymentOps.Evaluation thresholds --investigations <report.json> --retrieval <report.json> --out <file>
//        PaymentOps.Evaluation kill-test --agent <report.json> --challenger <report.json> --out <file> --summary <file>
//        PaymentOps.Evaluation retrieval-compare --endpoint <url> --ai <dir> --dataset <file> --sources <dir> --out <file> --summary <file>

using PaymentOps.Evaluation.Commands;

return args switch
{
    ["investigation-eval", .. var rest] => await InvestigationEvalCommand.RunAsync(rest),
    ["retrieval-eval", .. var rest] => await RetrievalEvalCommand.RunAsync(rest),
    ["thresholds", .. var rest] => await ThresholdsCommand.RunAsync(rest),
    ["kill-test", .. var rest] => await KillTestCommand.RunAsync(rest),
    ["retrieval-compare", .. var rest] => await RetrievalCompareCommand.RunAsync(rest),
    _ => Usage(),
};

static int Usage()
{
    Console.Error.WriteLine("Usage: PaymentOps.Evaluation investigation-eval | retrieval-eval | thresholds | kill-test | retrieval-compare [options]; see the header of Program.cs.");
    return ExitCodes.InvalidInput;
}
