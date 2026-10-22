"""Generate the small code-native graph icon used by the SolidWorks task pane."""
import pathlib
import struct
root=pathlib.Path(__file__).resolve().parents[1]
size=32
pixels=[]
for y in range(size-1,-1,-1):
    row=bytearray()
    for x in range(size):
        node=any((x-cx)**2+(y-cy)**2<=10 for cx,cy in ((8,8),(24,8),(8,24),(24,24)))
        line=(7<=x<=25 and (abs(y-8)<=1 or abs(y-24)<=1)) or (7<=y<=25 and (abs(x-8)<=1 or abs(x-24)<=1)) or (8<=x<=24 and abs(x-y)<=1)
        row.extend((239,248,240) if node or line else (94,116,20))
    pixels.append(row)
body=b''.join(pixels)
header=b'BM'+struct.pack('<IHHI',54+len(body),0,0,54)+struct.pack('<IiiHHIIiiII',40,size,size,1,24,0,len(body),2835,2835,0,0)
(root/'web'/'icon.bmp').write_bytes(header+body)
