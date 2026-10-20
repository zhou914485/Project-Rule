#!/bin/bash
# 用法: mcpcall.sh <toolName> [jsonArgs] < C#代码   （无 jsonArgs 时按 execute_code 处理）
TOOL="$1"; ARGJSON="$2"
MCP=http://localhost:8080/mcp
T=/tmp/_mcpc
SID=$(curl -s -m 8 -D $T.h -o /dev/null -X POST $MCP -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  -d '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"c","version":"1"}}}'; grep -i "mcp-session-id" $T.h | tr -d '\r' | awk '{print $2}')
[ -z "$SID" ] && { echo "!! init failed"; exit 1; }
curl -s -m 5 -X POST $MCP -H "Content-Type: application/json" -H "Accept: application/json, text/event-stream" \
  -H "Mcp-Session-Id: $SID" -d '{"jsonrpc":"2.0","method":"notifications/initialized"}' >/dev/null
if [ -n "$ARGJSON" ]; then
  printf '%s' "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/call\",\"params\":{\"name\":\"$TOOL\",\"arguments\":$ARGJSON}}" > $T.json
else
  python -c "
import json,sys
req={'jsonrpc':'2.0','id':2,'method':'tools/call','params':{'name':sys.argv[1],'arguments':{'action':'execute','compiler':'auto','code':sys.stdin.read()}}}
sys.stdout.write(json.dumps(req))
" "$TOOL" > $T.json
fi
curl -s -m 120 -X POST $MCP -H "Content-Type: application/json" -H "Accept: application/json, text/event-stream" \
  -H "Mcp-Session-Id: $SID" --data-binary @$T.json | sed -n 's/^data: //p' | tail -1
