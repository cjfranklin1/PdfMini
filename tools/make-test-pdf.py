#!/usr/bin/env python3
"""生成 PdfMini 的测试素材（纯标准库，不依赖任何第三方包）。

用法: python3 tools/make-test-pdf.py <输出目录>
生成: sample6.pdf(6页) extra3.pdf(3页) outline.pdf(4页+3条书签)
      ticket2.pdf(2页 176x100mm 横版，排版打印测试用)
"""
import os
import sys


def build_pdf(path, page_count, title_prefix, outline=None, page_size=(595, 842)):
    """outline: [(title, page_index_0based, [children...]), ...]"""
    parts = []

    def add(body):
        parts.append(body)
        return len(parts)  # object number

    def reserve():
        parts.append(b"")
        return len(parts)

    catalog_num = add(b"")          # 1，最后回填
    pages_num = add(b"")            # 2，最后回填
    font_num = add(b"<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>")

    page_nums = []
    w = int(page_size[0])
    h = int(page_size[1])
    for i in range(page_count):
        page_num = reserve()
        content_num = reserve()
        # 内容坐标按页面尺寸缩放（基准 A4 595x842），这样小车票页也能正常显示
        sy = h / 842.0
        sx = w / 595.0
        m = min(sy, sx)          # 字号按较小比例缩，避免溢出
        def T(size, x, y, text):
            return ("BT /F1 %.1f Tf %.1f %.1f Td (%s) Tj ET"
                    % (max(5.0, size * m), x * sx, y * sy, text))

        lines = [
            T(28, 72, 760, "%s - Page %d" % (title_prefix, i + 1)),
            T(14, 72, 700, "keyword-alpha line on page %d" % (i + 1)),
            T(11, 72, 640, "Outline test page %d / %d" % (i + 1, page_count)),
        ]
        for k in range(6):
            lines.append(T(11, 72, 600 - k * 24, "Body line %d on page %d." % (k + 1, i + 1)))
        stream = "\n".join(lines).encode("latin-1")
        parts[page_num - 1] = (
            "<< /Type /Page /Parent %d 0 R /MediaBox [0 0 %d %d] "
            "/Resources << /Font << /F1 %d 0 R >> >> /Contents %d 0 R >>"
            % (pages_num, int(page_size[0]), int(page_size[1]), font_num, content_num)
        ).encode()
        parts[content_num - 1] = (
            b"<< /Length " + str(len(stream)).encode() + b" >>\nstream\n" + stream + b"\nendstream"
        )
        page_nums.append(page_num)

    outlines_num = 0
    if outline:
        outlines_num = reserve()
        flat = []  # (obj_num, title, page_idx, parent_num, children_objs, next_num, count)

        def make_items(items, parent_num):
            nums = []
            for title, page_idx, kids in items:
                n = reserve()
                nums.append((n, title, page_idx, kids))
            for idx, (n, title, page_idx, kids) in enumerate(nums):
                child_nums = make_items(kids, n) if kids else []
                next_num = nums[idx + 1][0] if idx + 1 < len(nums) else 0
                first_child = child_nums[0] if child_nums else 0
                last_child = child_nums[-1] if child_nums else 0
                count = -(len(child_nums) + 1) if child_nums else 0  # 折叠显示
                body = "<< /Title %s /Parent %d 0 R /Dest [%d 0 R /Fit]" % (
                    title, parent_num, page_nums[page_idx])
                if next_num:
                    body += " /Next %d 0 R" % next_num
                if child_nums:
                    body += " /First %d 0 R /Last %d 0 R /Count %d" % (first_child, last_child, count)
                body += " >>"
                parts[n - 1] = body.encode("latin-1")
                flat.append(n)
            return [x[0] for x in nums]

        roots = make_items(outline, outlines_num)
        total = count_items(outline)
        parts[outlines_num - 1] = (
            "<< /Type /Outlines /First %d 0 R /Last %d 0 R /Count %d >>"
            % (roots[0], roots[-1], total)
        ).encode()

    catalog = "<< /Type /Catalog /Pages %d 0 R" % pages_num
    if outlines_num:
        catalog += " /Outlines %d 0 R" % outlines_num
    catalog += " >>"
    parts[catalog_num - 1] = catalog.encode()

    parts[pages_num - 1] = (
        "<< /Type /Pages /Kids [" + " ".join("%d 0 R" % p for p in page_nums) + "] /Count %d >>" % page_count
    ).encode()

    out = bytearray(b"%PDF-1.4\n%\xe2\xe3\xcf\xd3\n")
    offsets = []
    for idx, body in enumerate(parts):
        offsets.append(len(out))
        out += str(idx + 1).encode() + b" 0 obj\n" + body + b"\nendobj\n"
    xref = len(out)
    out += b"xref\n0 " + str(len(parts) + 1).encode() + b"\n0000000000 65535 f \n"
    for off in offsets:
        out += ("%010d 00000 n \n" % off).encode()
    out += (b"trailer\n<< /Size " + str(len(parts) + 1).encode() + b" /Root 1 0 R >>\nstartxref\n"
            + str(xref).encode() + b"\n%%EOF\n")
    with open(path, "wb") as f:
        f.write(bytes(out))
    print("%s  %d bytes  %d pages  %d outline items" % (path, len(out), page_count, count_items(outline or [])))


def count_items(items):
    return sum(1 + count_items(k) for _, _, k in items)


def main():
    out_dir = sys.argv[1] if len(sys.argv) > 1 else "test"
    os.makedirs(out_dir, exist_ok=True)

    build_pdf(os.path.join(out_dir, "sample6.pdf"), 6, "PdfMini Test")
    build_pdf(os.path.join(out_dir, "extra3.pdf"), 3, "Extra Doc")

    # 「第一回」-> 第1页，其子项「1.1 节」-> 第2页；「Chapter 2」-> 第3页
    outline = [
        ("FEFF7B2C4E0056DE", 0, [("(Section 1.1)", 1, [])]),   # 第一回
        ("(Chapter 2)", 2, []),
        ("(Chapter 3)", 3, []),
    ]
    # 标题写成 <FEFF...> 十六进制串（UTF-16BE）
    outline = [
        ("<FEFF7B2C4E0056DE>", 0, [("(Section 1.1)", 1, [])]),
        ("(Chapter 2)", 2, []),
        ("(Chapter 3)", 3, []),
    ]
    build_pdf(os.path.join(out_dir, "outline.pdf"), 4, "Outline Demo", outline)

    # 排版打印（N-up）测试素材：模拟火车票报销凭证
    # 176 x 100 mm 横版（火车票报销凭证的实际尺寸），2 页 → 应能排进一张 A4
    build_pdf(os.path.join(out_dir, "ticket2.pdf"), 2,
              "Railway Ticket Reimbursement",
              page_size=(int(176 / 25.4 * 72), int(100 / 25.4 * 72)))


if __name__ == "__main__":
    main()
