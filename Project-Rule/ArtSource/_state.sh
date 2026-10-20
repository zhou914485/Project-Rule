#!/bin/bash
MCP=http://localhost:8080/mcp
SID=$(curl -s -m 8 -D /tmp/_st.h -o /dev/null -X POST $MCP -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  -d '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"c","version":"1"}}}'; grep -i "mcp-session-id" /tmp/_st.h | tr -d '\r' | awk '{print $2}')
curl -s -m 5 -X POST $MCP -H "Content-Type: application/json" -H "Accept: application/json, text/event-stream" \
  -H "Mcp-Session-Id: $SID" -d '{"jsonrpc":"2.0","method":"notifications/initialized"}' >/dev/null
curl -s -m 30 -X POST $MCP -H "Content-Type: application/json" -H "Accept: application/json, text/event-stream" \
  -H "Mcp-Session-Id: $SID" --data-binary '{"jsonrpc":"2.0","id":5,"method":"resources/read","params":{"uri":"mcpforunity://editor/state"}}' \
  | sed -n 's/^data: //p' | tail -1
