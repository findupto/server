import 'package:flutter/material.dart';
import '../../core/api_client.dart';

class CustomerChatPage extends StatefulWidget {
  const CustomerChatPage({super.key, required this.api});
  final PosApiClient api;
  @override State<CustomerChatPage> createState() => _CustomerChatPageState();
}
class _CustomerChatPageState extends State<CustomerChatPage> {
  final text = TextEditingController();
  int? conversationId;
  List<dynamic> items = [];
  bool busy = true;
  Future<void> load() async {
    try { final cs = await widget.api.conversations(); if (cs.isNotEmpty) conversationId = cs.first['id']; items = conversationId == null ? [] : await widget.api.messages(conversationId!); } finally { if (mounted) setState(() => busy = false); }
  }
  Future<void> send() async { final value = text.text.trim(); if (value.isEmpty) return; if (conversationId == null) { final c = await widget.api.createConversation(['customer:${(await widget.api.token())?.hashCode ?? 0}', 'CP'], title: 'Customer support'); conversationId = c['id']; } await widget.api.sendMessage(conversationId!, value); text.clear(); items = await widget.api.messages(conversationId!); if (mounted) setState(() {}); }
  @override void initState() { super.initState(); load(); }
  @override Widget build(BuildContext context) => Scaffold(appBar: AppBar(title: const Text('Chat with Counter')), body: busy ? const Center(child: CircularProgressIndicator()) : Column(children: [Expanded(child: ListView.builder(itemCount: items.length, itemBuilder: (_, i) { final m = items[i]; return ListTile(title: Text(m['text'] ?? ''), subtitle: Text(m['senderUsername'] ?? '')); })), SafeArea(child: Row(children: [Expanded(child: TextField(controller: text, decoration: const InputDecoration(hintText: 'Message counter'))), IconButton(onPressed: send, icon: const Icon(Icons.send))]))]));
}
