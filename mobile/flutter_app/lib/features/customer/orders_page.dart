import 'package:flutter/material.dart';
import '../../core/api_client.dart';

class CustomerOrdersPage extends StatefulWidget {
  const CustomerOrdersPage({super.key, required this.api});
  final PosApiClient api;
  @override State<CustomerOrdersPage> createState() => _CustomerOrdersPageState();
}
class _CustomerOrdersPageState extends State<CustomerOrdersPage> {
  List<dynamic> orders = [];
  @override void initState() { super.initState(); widget.api.orders().then((v) { if (mounted) setState(() => orders = v); }); }
  @override Widget build(BuildContext context) => Scaffold(appBar: AppBar(title: const Text('My Orders')), body: RefreshIndicator(onRefresh: () async { final v = await widget.api.orders(); if (mounted) setState(() => orders = v); }, child: ListView.builder(itemCount: orders.length, itemBuilder: (_, i) { final o = orders[i]; return Card(child: ListTile(title: Text('Order #${o['id']}'), subtitle: Text('${o['orderType']} • ${o['status']}'), trailing: Text('Rs. ${o['total']}'))); }))); 
}
