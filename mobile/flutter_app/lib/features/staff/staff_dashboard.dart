import 'package:flutter/material.dart';
import '../../core/api_client.dart';

class StaffDashboard extends StatefulWidget {
  const StaffDashboard({super.key, required this.api, required this.role});
  final PosApiClient api; final String role;
  @override State<StaffDashboard> createState() => _StaffDashboardState();
}
class _StaffDashboardState extends State<StaffDashboard> {
  List<dynamic> orders = []; bool loading = true;
  String get endpoint => switch (widget.role.toLowerCase()) { 'kitchen' => '/api/kitchen/orders', 'rider' => '/api/rider/deliveries', _ => '/api/waiter/orders' };
  Future<void> load() async { try { final value = await widget.api.getList(endpoint); if (mounted) setState(() => orders = value); } finally { if (mounted) setState(() => loading = false); } }
  Future<void> update(int id, String status) async { await widget.api.patch('/api/${widget.role.toLowerCase() == 'rider' ? 'rider/orders' : 'kitchen/orders'}/$id/status', {'status': status}); await load(); }
  @override void initState() { super.initState(); load(); }
  @override Widget build(BuildContext context) => Scaffold(appBar: AppBar(title: Text('${widget.role} Dashboard')), body: loading ? const Center(child: CircularProgressIndicator()) : RefreshIndicator(onRefresh: load, child: ListView.builder(itemCount: orders.length, itemBuilder: (_, i) { final o = orders[i]; final id = o['id'] as int; final status = o['status'] ?? ''; return Card(child: ListTile(title: Text('Order #$id • ${o['orderType'] ?? ''}'), subtitle: Text('Rs. ${o['total'] ?? 0} • $status'), trailing: _actions(id, status))); }))); 
  Widget _actions(int id, String status) { if (widget.role.toLowerCase() == 'kitchen') { final next = status == 'New' ? 'Accepted' : status == 'Accepted' ? 'Preparing' : status == 'Preparing' ? 'Ready' : null; return next == null ? const SizedBox() : IconButton(onPressed: () => update(id, next), icon: const Icon(Icons.arrow_forward)); } if (widget.role.toLowerCase() == 'rider') { final next = status == 'Ready' ? 'OutForDelivery' : status == 'OutForDelivery' ? 'Completed' : null; return next == null ? const SizedBox() : IconButton(onPressed: () => update(id, next), icon: const Icon(Icons.local_shipping)); } return status == 'Ready' ? IconButton(onPressed: () async { await widget.api.post('/api/waiter/orders/$id/serve', {}); await load(); }, icon: const Icon(Icons.check_circle)) : const SizedBox(); }
}
