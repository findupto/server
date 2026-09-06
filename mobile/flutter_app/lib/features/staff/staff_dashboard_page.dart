import 'package:flutter/material.dart';
import '../../core/api_client.dart';

class StaffDashboardPage extends StatelessWidget {
  const StaffDashboardPage({super.key, required this.api, required this.role});
  final PosApiClient api;
  final String role;

  List<_Action> get actions {
    switch (role.toLowerCase()) {
      case 'kitchen': return const [_Action('Kitchen Orders', Icons.restaurant, '/api/kitchen/orders')];
      case 'waiter': return const [_Action('Waiter Orders', Icons.room_service, '/api/waiter/orders')];
      case 'rider': return const [_Action('Deliveries', Icons.delivery_dining, '/api/rider/deliveries')];
      default: return const [
        _Action('Orders', Icons.receipt_long, '/api/orders'),
        _Action('Payments', Icons.payments, '/api/orders'),
        _Action('Products', Icons.inventory_2, '/api/products'),
        _Action('Promotions', Icons.local_offer, '/api/promotions/all'),
        _Action('Staff', Icons.groups, '/api/users'),
      ];
    }
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(title: Text('$role Dashboard')),
    body: GridView.builder(
      padding: const EdgeInsets.all(16),
      gridDelegate: const SliverGridDelegateWithMaxCrossAxisExtent(maxCrossAxisExtent: 240, mainAxisExtent: 130, crossAxisSpacing: 12, mainAxisSpacing: 12),
      itemCount: actions.length,
      itemBuilder: (_, i) => Card(child: InkWell(onTap: () => Navigator.push(context, MaterialPageRoute(builder: (_) => StaffListPage(api: api, title: actions[i].title, path: actions[i].path))), child: Center(child: Column(mainAxisSize: MainAxisSize.min, children: [Icon(actions[i].icon, size: 34), const SizedBox(height: 8), Text(actions[i].title)]))),
    ),
  );
}

class StaffListPage extends StatefulWidget {
  const StaffListPage({super.key, required this.api, required this.title, required this.path});
  final PosApiClient api; final String title; final String path;
  @override State<StaffListPage> createState() => _StaffListPageState();
}
class _StaffListPageState extends State<StaffListPage> {
  dynamic data; String? error;
  @override void initState() { super.initState(); load(); }
  Future<void> load() async { try { final value = await widget.api.get(widget.path); if (mounted) setState(() => data = value); } catch (e) { if (mounted) setState(() => error = e.toString()); } }
  @override Widget build(BuildContext context) => Scaffold(appBar: AppBar(title: Text(widget.title)), body: error != null ? Center(child: Text(error!)) : data == null ? const Center(child: CircularProgressIndicator()) : ListView.builder(itemCount: data is List ? data.length : 1, itemBuilder: (_, i) { final value = data is List ? data[i] : data; return ListTile(title: Text(value is Map ? (value['name'] ?? value['title'] ?? value['id']?.toString() ?? 'Item').toString() : value.toString()), subtitle: Text(value is Map ? value.entries.take(4).map((e) => '${e.key}: ${e.value}').join(' • ') : '')); }));
}
class _Action { const _Action(this.title, this.icon, this.path); final String title; final IconData icon; final String path; }
