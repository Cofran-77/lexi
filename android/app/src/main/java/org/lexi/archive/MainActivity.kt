package org.lexi.archive

import android.content.Intent
import android.os.Bundle
import android.print.PrintAttributes
import android.print.PrintManager
import android.webkit.WebView
import android.webkit.WebViewClient
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.activity.result.contract.ActivityResultContracts
import androidx.activity.viewModels
import org.lexi.archive.ui.LexiApp

class MainActivity : ComponentActivity() {
    private val vm: LexiViewModel by viewModels()
    private var exportKind = "json"
    private var exportIds: Set<Long>? = null
    private var restoring = false
    private var printView: WebView? = null
    private val createFile = registerForActivityResult(ActivityResultContracts.StartActivityForResult()) { result ->
        if (result.resultCode == RESULT_OK) result.data?.data?.let { vm.writeFile(it, exportKind, exportIds) }
    }
    private val openFile = registerForActivityResult(ActivityResultContracts.OpenDocument()) { uri ->
        if (uri != null) vm.importFile(uri, restoring)
    }
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        exportKind = savedInstanceState?.getString("exportKind") ?: "json"
        exportIds = savedInstanceState?.getLongArray("exportIds")?.toSet()
        restoring = savedInstanceState?.getBoolean("restoring") ?: false
        enableEdgeToEdge()
        setContent { LexiApp(vm, ::export, ::openDocument, ::print) }
        if (savedInstanceState == null) consumeIntent(intent)
    }
    override fun onSaveInstanceState(outState: Bundle) {
        outState.putString("exportKind", exportKind)
        exportIds?.let { outState.putLongArray("exportIds", it.toLongArray()) }
        outState.putBoolean("restoring", restoring)
        super.onSaveInstanceState(outState)
    }
    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent); setIntent(intent); consumeIntent(intent)
    }
    private fun consumeIntent(intent: Intent) {
        val text = when (intent.action) {
            Intent.ACTION_PROCESS_TEXT -> intent.getCharSequenceExtra(Intent.EXTRA_PROCESS_TEXT)
            Intent.ACTION_SEND -> intent.getCharSequenceExtra(Intent.EXTRA_TEXT)
            else -> null
        }
        intent.removeExtra(Intent.EXTRA_PROCESS_TEXT); intent.removeExtra(Intent.EXTRA_TEXT)
        if (text != null) vm.handleIncoming(text.toString())
    }
    private fun export(kind: String, ids: Set<Long>?) {
        exportKind = kind; exportIds = ids?.toSet()
        val ext = when (kind) { "html" -> "html"; "backup" -> "sqlite3"; else -> "json" }
        createFile.launch(Intent(Intent.ACTION_CREATE_DOCUMENT).apply {
            addCategory(Intent.CATEGORY_OPENABLE)
            type = when (kind) { "html" -> "text/html"; "backup" -> "application/octet-stream"; else -> "application/json" }
            putExtra(Intent.EXTRA_TITLE, "Lexi-${java.time.LocalDate.now()}.$ext")
        })
    }
    private fun openDocument(restore: Boolean) { restoring = restore; openFile.launch(arrayOf("*/*")) }
    private fun print(ids: Set<Long>?) {
        // Only the user-requested print preview uses Android's system WebView.
        // Main UI and all word cards are native Compose. No remote resources/JS.
        printView?.destroy()
        val view = WebView(this)
        printView = view
        view.settings.javaScriptEnabled = false
        view.settings.allowFileAccess = false
        view.settings.blockNetworkLoads = true
        view.webViewClient = object : WebViewClient() {
            override fun onPageFinished(v: WebView, url: String?) {
                (getSystemService(PRINT_SERVICE) as PrintManager).print("Lexi 私人词汇档案", v.createPrintDocumentAdapter("Lexi"), PrintAttributes.Builder().setMediaSize(PrintAttributes.MediaSize.ISO_A4).build())
            }
        }
        view.loadDataWithBaseURL(null, vm.printHtml(ids), "text/html", "UTF-8", null)
    }
    override fun onDestroy() { printView?.destroy(); printView = null; super.onDestroy() }
}
