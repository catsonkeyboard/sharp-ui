# 性能基线(自动生成)

- 每帧分配:5948 B(预算 8192;M3b 深度池后)
- 每帧 CPU:0.05 ms(预算 8)
- 场景:Counter,480×320,Release 测得(含 id+焦点+即时面板管线)

<!-- 2026-10-26 复测注记(M3d-ii Task 5,D3D11 直连后端,Veldrid 图形栈移除后 Release 重测):
     与替换前持平——旧后端(M3bcd 收官)实测 6008 B / 0.06 ms,本次 5948 B / 0.05 ms:
     分配 −1.0%,CPU 差在两位小数舍入内,均 <10% 无需归因(稳态分配由 build→layout→tessellate
     CPU 管线主导,后端中立;绘制路径零新分配:GCHandle 钉数组 + UpdateSubresource,Draw 内无 new)。
     注意:本文件由 Release PerfTests 全文重写,本注记随下次复测被覆盖。 -->

