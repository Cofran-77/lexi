package org.lexi.archive

import android.graphics.Color
import android.graphics.Paint
import android.graphics.Typeface
import android.graphics.pdf.PdfDocument
import android.text.Layout
import android.text.StaticLayout
import android.text.TextPaint
import org.lexi.archive.data.Entry
import java.io.OutputStream

/** Native, paginated A4 export. No HTML, network or print-service dependency. */
object PdfExporter {
    fun write(entries: List<Entry>, output: OutputStream) {
        val document = PdfDocument()
        var page: PdfDocument.Page? = null
        var number = 0
        var y = 0f
        val text = TextPaint(Paint.ANTI_ALIAS_FLAG).apply { color = Color.rgb(30, 42, 36); textSize = 11f; typeface = Typeface.create("sans-serif", Typeface.NORMAL) }
        fun finish() { page?.let { document.finishPage(it) }; page = null }
        fun next() {
            finish(); number++
            page = document.startPage(PdfDocument.PageInfo.Builder(595, 842, number).create())
            val header = Paint(text).apply { textSize = 10f; color = Color.rgb(85, 110, 96) }
            page!!.canvas.drawText("Lexi · 私人词汇档案", 40f, 33f, header)
            page!!.canvas.drawText("$number", 540f, 814f, header)
            y = 58f
        }
        fun paragraph(value: String, size: Float = 11f, bold: Boolean = false) {
            if (value.isBlank()) return
            text.textSize = size; text.typeface = Typeface.create("sans-serif", if (bold) Typeface.BOLD else Typeface.NORMAL)
            // Split by layout lines, so even one unusually long field can span pages.
            val layout = StaticLayout.Builder.obtain(value, 0, value.length, text, 515)
                .setAlignment(Layout.Alignment.ALIGN_NORMAL).setIncludePad(false).setLineSpacing(3f, 1f).build()
            for (line in 0 until layout.lineCount) {
                val top = layout.getLineTop(line); val bottom = layout.getLineBottom(line)
                val height = (bottom - top).toFloat()
                if (y + height > 784f) next()
                val canvas = page!!.canvas
                canvas.save(); canvas.clipRect(40f, y, 555f, y + height)
                canvas.translate(40f, y - top); layout.draw(canvas); canvas.restore()
                y += height
            }
            y += 6f
        }
        try {
            next()
            if (entries.isEmpty()) paragraph("暂无词条")
            entries.forEach { entry ->
                if (y > 680f) next()
                paragraph(entry.word, 17f, true)
                paragraph(entry.phonetic, 10f)
                paragraph(entry.translation)
                paragraph(entry.definition, 10f)
                paragraph(if (entry.archive.sourceTitle.isBlank()) "" else "来源：${entry.archive.sourceTitle}", 10f)
                paragraph(entry.archive.sourceExcerpt, 10f)
                paragraph(if (entry.notes.isBlank()) "" else "备注：${entry.notes}", 10f)
                paragraph(if (entry.archive.tags.isEmpty()) "" else "标签：${entry.archive.tags.joinToString(" · ")}", 10f)
                entry.aiResult?.let { ai ->
                    ai.examples.forEach { paragraph(it.english); paragraph(it.chinese, 10f) }
                    if (ai.synonyms.isNotEmpty()) paragraph("同义词：${ai.synonyms.joinToString(" · ")}", 10f)
                    if (ai.antonyms.isNotEmpty()) paragraph("反义词：${ai.antonyms.joinToString(" · ")}", 10f)
                    ai.phrases.forEach { paragraph("${it.en}  ${it.zh}", 10f) }
                }
                if (y + 35 > 784f) next()
                val pen = Paint(text).apply { style = Paint.Style.STROKE; strokeWidth = .6f }
                listOf(1, 2, 4, 7, 15).forEachIndexed { index, day ->
                    val x = 40f + index * 93f
                    page!!.canvas.drawRect(x, y + 2, x + 9, y + 11, pen)
                    page!!.canvas.drawText("第 $day 天", x + 15, y + 11, text)
                }
                y += 29
                page!!.canvas.drawLine(40f, y, 555f, y, pen); y += 16
            }
            finish(); document.writeTo(output)
        } finally { document.close() }
    }
}
